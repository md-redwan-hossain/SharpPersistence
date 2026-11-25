using System.Reflection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DoctorWiringValidator(
    DbMigratorOptions options,
    ISqlDialectMetadata dialect,
    MigrationConnectionOpener connectionOpener)
{
    public List<DoctorCheckItem> Validate(
        string? registryFilter,
        IReadOnlyList<Assembly>? discoveredAssemblies)
    {
        var checks = new List<DoctorCheckItem>
        {
            new(
                DoctorSeverity.Pass,
                $"Migrator configured (Database: {options.Dialect}, Naming: {options.IdentifierCase})")
        };

        if (options.ConnectionFactory is not null)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                "Database connection configured via custom connection factory"));
        }
        else if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            if (options.ProviderFactory is not null || connectionOpener.CanResolveProviderFactory())
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Pass,
                    "Database connection string and driver verified"));
            }
            else
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Connection string is set, but no database driver could be loaded for '{options.Dialect}'.",
                    Hint: dialect.ProviderPackageHint));
            }
        }
        else
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                "No database connection configured in AddDbMigrator.",
                Hint: "Call UsePostgreSql(...) or UseOracle(...) with a connection string or connection factory."));
        }

        if (options.Registries.Count == 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                "No IEntityRegistry instances are registered in DI.",
                Hint: "Call AddRegistry<T>() or AddGeneratedEntityRegistries() inside AddDbMigrator."));
        }
        else
        {
            var names = string.Join(", ", options.Registries.Select(r => r.GetType().Name));
            var label = options.Registries.Count == 1 ? "registry" : "registries";
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                $"{options.Registries.Count} {label} wired: {names}"));
        }

        if (!string.IsNullOrWhiteSpace(registryFilter) &&
            !options.Registries.Any(r => string.Equals(r.GetType().Name, registryFilter, StringComparison.OrdinalIgnoreCase)))
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Registry '{registryFilter}' is not registered in DI.",
                Hint: "Check the --registry name or register it inside AddDbMigrator."));
        }

        if (discoveredAssemblies is { Count: > 0 })
        {
            var registeredTypes = options.Registries.Select(r => r.GetType()).ToHashSet();
            foreach (var assembly in discoveredAssemblies)
            {
                foreach (var type in GetLoadableTypes(assembly))
                {
                    if (type.IsAbstract || type.IsInterface || !type.IsClass ||
                        !typeof(IEntityRegistry).IsAssignableFrom(type) ||
                        registeredTypes.Contains(type))
                    {
                        continue;
                    }

                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Warning,
                        $"Unregistered registry found: {type.Name} ({assembly.GetName().Name})",
                        Hint: "Register it in DI or call AddGeneratedEntityRegistries()."));
                }
            }
        }

        return checks;
    }

    internal static IEnumerable<Type> GetLoadableTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
    }
}
