using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli;

internal sealed record LoadedMigrationContext(
    object? ServiceScopeOwner,
    MigrationManager Manager,
    string RootPath,
    string HostName,
    string StartupProjectDisplayPath,
    string BootstrapSource,
    IReadOnlyList<Assembly> LoadedAssemblies);

internal static class TargetAssemblyLoader
{
    public static (object? ServiceScopeOwner, MigrationManager Manager, string RootPath) CreateManager(SharedSettings settings)
    {
        var ctx = CreateManagerContext(settings);
        return (ctx.ServiceScopeOwner, ctx.Manager, ctx.RootPath);
    }

    public static LoadedMigrationContext CreateManagerContext(SharedSettings settings)
    {
        var (csprojPath, assemblyPath, startupDir) = ResolveStartupAssembly(settings.StartupProjectPath, settings.NoBuild);
        var startupDisplayPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), csprojPath);
        if (startupDisplayPath.StartsWith("..", StringComparison.Ordinal))
        {
            startupDisplayPath = csprojPath;
        }
        var destinationRoot = ResolveDestinationDirectory(settings.DestinationProjectPath, startupDir);
        var outputRoot = ResolveOutputDirectory(settings.OutputDir, settings.DestinationProjectPath is not null ? destinationRoot : null, destinationRoot);

        var alc = new TargetLoadContext(assemblyPath);
        var primaryAssembly = alc.LoadFromAssemblyPath(assemblyPath);
        var hostName = primaryAssembly.GetName().Name ?? Path.GetFileNameWithoutExtension(assemblyPath);
        var loadedAssemblies = new List<Assembly> { primaryAssembly };

        var targetDir = Path.GetDirectoryName(assemblyPath)!;
        foreach (var refName in primaryAssembly.GetReferencedAssemblies())
        {
            if (IsFrameworkOrCoreAssembly(refName.Name))
            {
                continue;
            }

            var candidate = Path.Combine(targetDir, $"{refName.Name}.dll");
            if (File.Exists(candidate))
            {
                try
                {
                    loadedAssemblies.Add(alc.LoadFromAssemblyPath(candidate));
                }
                catch
                {
                    // Ignore unloadable satellite/native assemblies
                }
            }
        }

        foreach (var providerDll in new[] { "Npgsql.dll", "Oracle.ManagedDataAccess.dll" })
        {
            var providerPath = Path.Combine(targetDir, providerDll);
            if (File.Exists(providerPath))
            {
                try
                {
                    alc.LoadFromAssemblyPath(providerPath);
                }
                catch
                {
                    // Ignore if already loaded or incompatible
                }
            }
        }

        // Tier 1: Optional IDesignTimeMigrationFactory fallback (for class libraries or custom offline config)
        var factoryType = loadedAssemblies
            .SelectMany(DoctorWiringValidator.GetLoadableTypes)
            .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface && typeof(IDesignTimeMigrationFactory).IsAssignableFrom(t));

        if (factoryType is not null)
        {
            var designFactory = (IDesignTimeMigrationFactory)Activator.CreateInstance(factoryType)!;
            var services = new ServiceCollection();
            services.AddDbMigrator(options =>
            {
                designFactory.Configure(options, Environment.GetCommandLineArgs());
                options.UseRootPath(outputRoot);
            });

            var provider = services.BuildServiceProvider();
            var manager = (MigrationManager)provider.GetRequiredService<IMigrationManager>();
            return new LoadedMigrationContext(provider, manager, outputRoot, hostName, startupDisplayPath, factoryType.Name, loadedAssemblies);
        }

        // Tier 2: Resolve IHost / IServiceProvider from Program.Main via Microsoft.Extensions.Hosting DiagnosticListener
        var host = ResolveHostFromEntryPoint(primaryAssembly, startupDir);
        var hostManager = host.Services.GetService<IMigrationManager>() as MigrationManager
            ?? throw new InvalidOperationException(
                $"'{primaryAssembly.GetName().Name}' built an IHost, but IMigrationManager is not registered in DI. " +
                "Call builder.Services.AddDbMigrator(...) in Program.cs or add an IDesignTimeMigrationFactory.");

        var options = host.Services.GetRequiredService<MigrationManagerOptions>();
        options.OverrideRootPath(outputRoot);

        return new LoadedMigrationContext(host, hostManager, outputRoot, hostName, startupDisplayPath, "Program.cs", loadedAssemblies);
    }

    public static async ValueTask DisposeAsync(object? owner)
    {
        switch (owner)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private static IHost ResolveHostFromEntryPoint(Assembly primaryAssembly, string startupDir)
    {
        var entryPoint = primaryAssembly.EntryPoint
            ?? throw new InvalidOperationException(
                $"'{primaryAssembly.GetName().Name}' has no entry point (Program.Main) and no IDesignTimeMigrationFactory was found.");

        IHost? capturedHost = null;
        using var observer = new HostBuiltObserver(host =>
        {
            capturedHost = host;
            throw new HostFactoryAbortedException();
        });
        using var subscription = DiagnosticListener.AllListeners.Subscribe(observer);

        var prevCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(startupDir);
            var parameters = entryPoint.GetParameters();
            var invokeArgs = parameters.Length == 0 ? null : new object?[] { Array.Empty<string>() };
            var result = entryPoint.Invoke(null, invokeArgs);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is HostFactoryAbortedException)
        {
            // Expected abort right after HostBuilt
        }
        catch (HostFactoryAbortedException)
        {
            // Expected abort right after HostBuilt
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
        }

        return capturedHost ?? throw new InvalidOperationException(
            $"Could not capture IHost from '{primaryAssembly.GetName().Name}'. " +
            "Ensure Program.cs uses WebApplication.CreateBuilder / Host.CreateApplicationBuilder, or implement IDesignTimeMigrationFactory.");
    }

    private sealed class HostFactoryAbortedException : Exception;

    private sealed class HostBuiltObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly Action<IHost> _onHostBuilt;
        private IDisposable? _listenerSubscription;

        public HostBuiltObserver(Action<IHost> onHostBuilt) => _onHostBuilt = onHostBuilt;

        public void OnNext(DiagnosticListener listener)
        {
            if (string.Equals(listener.Name, "Microsoft.Extensions.Hosting", StringComparison.Ordinal))
            {
                _listenerSubscription = listener.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (string.Equals(value.Key, "HostBuilt", StringComparison.Ordinal) && value.Value is IHost host)
            {
                _onHostBuilt(host);
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() => _listenerSubscription?.Dispose();
    }

    private static (string CsprojPath, string AssemblyPath, string StartupDir) ResolveStartupAssembly(string? startupArg, bool noBuild)
    {
        var csprojPath = ResolveCsprojOrDirectory(
            startupArg ?? Directory.GetCurrentDirectory(),
            "startup");

        if (!noBuild)
        {
            BuildStartupProject(csprojPath);
        }

        var startupDir = Path.GetDirectoryName(csprojPath)!;
        var assemblyName = ReadAssemblyName(csprojPath);
        var binDir = Path.Combine(startupDir, "bin");

        if (!Directory.Exists(binDir))
        {
            throw new InvalidOperationException(
                $"No 'bin' directory found for '{csprojPath}'. Run 'dotnet build' on the startup project first.");
        }

        var dll = Directory.EnumerateFiles(binDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (dll is null)
        {
            throw new FileNotFoundException(
                $"Compiled assembly '{assemblyName}.dll' not found under '{binDir}'. Run 'dotnet build' first.");
        }

        return (csprojPath, dll, startupDir);
    }

    private static void BuildStartupProject(string csprojPath)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" --nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'dotnet build'.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Building '{csprojPath}' failed (exit code {process.ExitCode}).{Environment.NewLine}{stdout}{stderr}");
        }
    }

    private static string ResolveDestinationDirectory(string? destinationArg, string fallbackDir)
    {
        if (string.IsNullOrWhiteSpace(destinationArg))
        {
            return fallbackDir;
        }

        var full = Path.GetFullPath(destinationArg);
        if (full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(full)!;
        }

        return full;
    }

    private static string ResolveOutputDirectory(string? outputDirArg, string? explicitDestDir, string fallbackRoot)
    {
        if (string.IsNullOrWhiteSpace(outputDirArg))
        {
            return fallbackRoot;
        }

        if (Path.IsPathRooted(outputDirArg))
        {
            return Path.GetFullPath(outputDirArg);
        }

        var baseDir = explicitDestDir ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, outputDirArg));
    }

    private static string ResolveCsprojOrDirectory(string pathOrDir, string role)
    {
        var full = Path.GetFullPath(pathOrDir);
        if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        if (Directory.Exists(full))
        {
            var files = Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly);
            return files.Length switch
            {
                0 => throw new InvalidOperationException(
                    $"No .csproj file found in '{full}'. Pass --{role}-project <path.csproj>."),
                > 1 => throw new InvalidOperationException(
                    $"Multiple .csproj files found in '{full}'. Pass --{role}-project <path.csproj> to specify one."),
                _ => files[0]
            };
        }

        throw new FileNotFoundException($"Project file or directory not found: '{full}'.");
    }

    private static string ReadAssemblyName(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var explicitName = doc.Descendants("AssemblyName").FirstOrDefault()?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }
        }
        catch
        {
            // Fall back to project file name
        }

        return Path.GetFileNameWithoutExtension(csprojPath);
    }

    private static bool IsFrameworkOrCoreAssembly(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SharpPersistence", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SharpPersistence.Migrator", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SharpPersistence.Migrator.EfCore", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Humanizer", StringComparison.OrdinalIgnoreCase);

    private sealed class TargetLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public TargetLoadContext(string targetPath) : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(targetPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (Default.Assemblies.Any(a =>
                    string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }
}
