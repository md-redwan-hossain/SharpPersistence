using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Cli.Config;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Services;
using Spectre.Console;

namespace SharpPersistence.Migrator.Cli;

internal sealed record LoadedMigrationContext(
    object? ServiceScopeOwner,
    DbMigrator Manager,
    string RootPath,
    string HostName,
    string StartupProjectDisplayPath,
    string BootstrapSource,
    IReadOnlyList<Assembly> LoadedAssemblies);

internal static class TargetAssemblyLoader
{
    public static (object? ServiceScopeOwner, DbMigrator Manager, string RootPath) CreateManager(SharedSettings settings)
    {
        var ctx = CreateManagerContext(settings);
        return (ctx.ServiceScopeOwner, ctx.Manager, ctx.RootPath);
    }

    public static LoadedMigrationContext CreateManagerContext(SharedSettings settings)
    {
        var errConsole = CreateStderrConsole();
        var totalSw = Stopwatch.StartNew();

        var resolvedPaths = ProjectResolutionService.Resolve(settings);
        var csprojPath = ResolveCsprojOrDirectory(
            resolvedPaths.StartupProjectPath ?? Directory.GetCurrentDirectory(),
            "startup");
        var startupDisplayPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), csprojPath);
        if (startupDisplayPath.StartsWith("..", StringComparison.Ordinal))
        {
            startupDisplayPath = csprojPath;
        }

        double? buildSeconds = null;
        if (!settings.NoBuild)
        {
            var buildSw = Stopwatch.StartNew();
            RunWithStatus(errConsole, $"Building [cyan]{Markup.Escape(startupDisplayPath)}[/]...", () =>
            {
                BuildStartupProject(csprojPath);
            });
            buildSw.Stop();
            buildSeconds = buildSw.Elapsed.TotalSeconds;
        }

        var (assemblyPath, startupDir) = LocateStartupAssembly(csprojPath);
        var destinationRoot = ResolveDestinationDirectory(resolvedPaths.DestinationProjectPath, startupDir);
        var outputRoot = ResolveOutputDirectory(settings.OutputDir, resolvedPaths.DestinationProjectPath is not null ? destinationRoot : null, destinationRoot);

        var hostName = Path.GetFileNameWithoutExtension(assemblyPath);
        var loadedAssemblies = new List<Assembly>();

        var primaryAssembly = RunWithStatus(errConsole, $"Loading assemblies ([cyan]{Markup.Escape(hostName)}[/])...", () =>
        {
            var alc = new TargetLoadContext(assemblyPath);
            var primary = alc.LoadFromAssemblyPath(assemblyPath);
            hostName = primary.GetName().Name ?? hostName;
            loadedAssemblies.Add(primary);

            var targetDir = Path.GetDirectoryName(assemblyPath)!;
            var defaultAssemblyNames = AssemblyLoadContext.Default.Assemblies
                .Select(a => a.GetName().Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var refName in primary.GetReferencedAssemblies())
            {
                if (IsFrameworkOrCoreAssembly(refName.Name) ||
                    (refName.Name is not null && defaultAssemblyNames.Contains(refName.Name)))
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

            return primary;
        });

        // Tier 1: Optional IDesignTimeDbMigratorFactory fallback (for class libraries or custom offline config)
        var factoryType = loadedAssemblies
            .SelectMany(DoctorWiringValidator.GetLoadableTypes)
            .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface && typeof(IDesignTimeDbMigratorFactory).IsAssignableFrom(t));

        if (factoryType is not null)
        {
            var bootstrapSw = Stopwatch.StartNew();

            var (provider, manager) = RunWithStatus(errConsole, $"Bootstrapping IDbMigrator via [cyan]{Markup.Escape(factoryType.Name)}[/]...", () =>
            {
                var designFactory = (IDesignTimeDbMigratorFactory)Activator.CreateInstance(factoryType)!;
                var services = new ServiceCollection();
                services.AddDbMigrator(options =>
                {
                    designFactory.Configure(options, Environment.GetCommandLineArgs());
                    options.UseRootPath(outputRoot);
                });

                var sp = services.BuildServiceProvider();
                var mgr = (DbMigrator)sp.GetRequiredService<IDbMigrator>();
                return (sp, mgr);
            });

            bootstrapSw.Stop();
            WriteLoadedSummary(errConsole, startupDisplayPath, factoryType.Name, buildSeconds, bootstrapSw.Elapsed.TotalSeconds, totalSw.Elapsed.TotalSeconds);
            return new LoadedMigrationContext(provider, manager, outputRoot, hostName, startupDisplayPath, factoryType.Name, loadedAssemblies);
        }

        // Tier 2: Resolve IHost / IServiceProvider from Program.Main via Microsoft.Extensions.Hosting DiagnosticListener
        var hostSw = Stopwatch.StartNew();
        var host = RunWithStatus(errConsole, $"Bootstrapping IDbMigrator via [cyan]{Markup.Escape(hostName)}[/] (Program.cs)...", () =>
            ResolveHostFromEntryPoint(primaryAssembly, startupDir));
        hostSw.Stop();

        var hostManager = host.Services.GetService<IDbMigrator>() as DbMigrator
            ?? throw new InvalidOperationException(
                $"'{primaryAssembly.GetName().Name}' built an IHost, but IDbMigrator is not registered in DI. " +
                "Call builder.Services.AddDbMigrator(...) in Program.cs or add an IDesignTimeDbMigratorFactory.");

        var options = host.Services.GetRequiredService<DbMigratorOptions>();
        options.OverrideRootPath(outputRoot);

        WriteLoadedSummary(errConsole, startupDisplayPath, "Program.cs", buildSeconds, hostSw.Elapsed.TotalSeconds, totalSw.Elapsed.TotalSeconds);
        return new LoadedMigrationContext(host, hostManager, outputRoot, hostName, startupDisplayPath, "Program.cs", loadedAssemblies);
    }

    internal static IAnsiConsole CreateStderrConsole() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

    internal static T RunWithStatus<T>(IAnsiConsole errConsole, string markupMessage, Func<T> func)
    {
        if (Console.IsErrorRedirected)
        {
            errConsole.MarkupLine($"[grey]{markupMessage}[/]");
            return func();
        }

        return errConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start(markupMessage, _ => func());
    }

    internal static void RunWithStatus(IAnsiConsole errConsole, string markupMessage, Action action) =>
        RunWithStatus(errConsole, markupMessage, () => { action(); return 0; });

    private static void WriteLoadedSummary(
        IAnsiConsole errConsole,
        string startupDisplayPath,
        string bootstrapSource,
        double? buildSeconds,
        double bootstrapSeconds,
        double totalSeconds)
    {
        var buildPart = buildSeconds.HasValue
            ? $"Built {Markup.Escape(startupDisplayPath)} ({buildSeconds.Value.ToString("0.0", CultureInfo.InvariantCulture)}s) | "
            : string.Empty;

        errConsole.MarkupLine(
            $"[grey]{buildPart}Bootstrapped via {Markup.Escape(bootstrapSource)} ({bootstrapSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s, total {totalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s)[/]");
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
                $"'{primaryAssembly.GetName().Name}' has no entry point (Program.Main) and no IDesignTimeDbMigratorFactory was found.");

        IHost? capturedHost = null;
        using var observer = new HostBuiltObserver(host =>
        {
            capturedHost = host;
            throw new HostAbortedException();
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
        catch (TargetInvocationException ex) when (ex.InnerException is HostAbortedException)
        {
            // Expected abort right after HostBuilt
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
        catch (HostAbortedException)
        {
            // Expected abort right after HostBuilt
        }
        finally
        {
            Directory.SetCurrentDirectory(prevCwd);
        }

        return capturedHost ?? throw new InvalidOperationException(
            $"Could not capture IHost from '{primaryAssembly.GetName().Name}'. " +
            "Ensure Program.cs uses WebApplication.CreateBuilder / Host.CreateApplicationBuilder, or implement IDesignTimeDbMigratorFactory.");
    }

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

    private static (string AssemblyPath, string StartupDir) LocateStartupAssembly(string csprojPath)
    {
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

        return (dll, startupDir);
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Building '{csprojPath}' failed (exit code {process.ExitCode}).{Environment.NewLine}{stdoutTask.Result}{stderrTask.Result}");
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

    private static bool IsSharedCliAssembly(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Equals("SharpPersistence", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("SharpPersistence.Abstractions", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("SharpPersistence.Migrator", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("Humanizer", StringComparison.OrdinalIgnoreCase));

    private static bool IsFrameworkOrCoreAssembly(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
        IsSharedCliAssembly(name);

    private sealed class TargetLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public TargetLoadContext(string targetPath) : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(targetPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (IsSharedCliAssembly(assemblyName.Name) ||
                Default.Assemblies.Any(a =>
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
