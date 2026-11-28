using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class RegistryDependencyResolver
{
    public static IReadOnlyList<(IEntityRegistry Registry, DatabaseModel Model)> SortAndScanTopologically(DbMigratorOptions options) =>
        SortAndScanTopologically(
            options.Registries,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityReplacements,
            options.PostgreSql.Enums,
            options.SqlExpressionOptions);

    public static IReadOnlyList<IEntityRegistry> SortTopologically(DbMigratorOptions options) =>
        SortTopologically(
            options.Registries,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityReplacements,
            options.PostgreSql.Enums,
            options.SqlExpressionOptions);

    public static IReadOnlyList<IEntityRegistry> SortTopologically(
        IReadOnlyList<IEntityRegistry> registries,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null,
        bool pluralizeTableNames = false,
        IReadOnlyList<EntityConventionRule>? entityConventions = null,
        Func<Type, string, string>? tableNameConvention = null,
        IReadOnlyDictionary<Type, Type>? entityReplacements = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        SqlExpressionBuilderOptions? sqlExpressionOptions = null)
    {
        if (registries.Count <= 1)
        {
            return registries;
        }

        var sorted = SortAndScanTopologically(
            registries,
            dialect,
            identifierCase,
            propertyConventions,
            pluralizeTableNames,
            entityConventions,
            tableNameConvention,
            entityReplacements,
            pgEnums,
            sqlExpressionOptions);

        return sorted.Select(static s => s.Registry).ToList();
    }

    public static IReadOnlyList<(IEntityRegistry Registry, DatabaseModel Model)> SortAndScanTopologically(
        IReadOnlyList<IEntityRegistry> registries,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null,
        bool pluralizeTableNames = false,
        IReadOnlyList<EntityConventionRule>? entityConventions = null,
        Func<Type, string, string>? tableNameConvention = null,
        IReadOnlyDictionary<Type, Type>? entityReplacements = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        SqlExpressionBuilderOptions? sqlExpressionOptions = null)
    {
        if (registries.Count == 0)
        {
            return [];
        }

        var registryByName = new Dictionary<string, IEntityRegistry>(registries.Count, StringComparer.Ordinal);
        foreach (var registry in registries)
        {
            var regType = registry.GetType();
            if (registryByName.TryGetValue(regType.Name, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate entity registry short name '{regType.Name}' found for '{existing.GetType().FullName}' and '{regType.FullName}'. " +
                    "Rename one of the registry classes so each registry has a unique short class name.");
            }

            registryByName[regType.Name] = registry;
        }

        var scannedAll = SchemaScanner.ScanAllByRegistry(
            registries,
            dialect,
            identifierCase,
            targetRegistries: registries,
            propertyConventions: propertyConventions,
            pgEnums: pgEnums,
            sqlExpressionOptions: sqlExpressionOptions,
            pluralizeTableNames: pluralizeTableNames,
            entityConventions: entityConventions,
            tableNameConvention: tableNameConvention,
            entityReplacements: entityReplacements);

        if (registries.Count == 1)
        {
            return scannedAll;
        }

        var dialectMeta = MigrationDialectRegistry.Get(dialect);
        var ownerByEntityName = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownerByQualifiedTable = new Dictionary<(string Schema, string Table), string>();
        var excludedEntities = new HashSet<string>(StringComparer.Ordinal);
        var scannedSchemas = new Dictionary<string, DatabaseModel>(registries.Count, StringComparer.Ordinal);

        foreach (var (registry, schema) in scannedAll)
        {
            var regName = registry.GetType().Name;
            scannedSchemas[regName] = schema;

            foreach (var table in schema.Tables)
            {
                ownerByEntityName[table.EntityName] = regName;

                if (table.ExcludeFromMigrations)
                {
                    excludedEntities.Add(table.EntityName);
                    continue;
                }

                var key = (dialectMeta.NormalizeSchemaName(table.Schema), table.TableName);
                ownerByQualifiedTable.TryAdd(key, regName);
            }
        }

        var dependents = registryByName.Keys.ToDictionary(
            k => k,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var inDegree = registryByName.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);

        foreach (var (currentName, schema) in scannedSchemas)
        {
            foreach (var table in schema.Tables)
            {
                if (table.ExcludeFromMigrations)
                {
                    continue;
                }

                foreach (var fk in table.ForeignKeys)
                {
                    if (fk.IsCircular || string.IsNullOrWhiteSpace(fk.PrincipalEntityName))
                    {
                        continue;
                    }

                    string? principalName = null;
                    if (excludedEntities.Contains(fk.PrincipalEntityName))
                    {
                        var tableKey = (dialectMeta.NormalizeSchemaName(fk.PrincipalSchema), fk.PrincipalTable);
                        ownerByQualifiedTable.TryGetValue(tableKey, out principalName);
                    }
                    else
                    {
                        ownerByEntityName.TryGetValue(fk.PrincipalEntityName, out principalName);
                    }

                    if (principalName is not null &&
                        !string.Equals(principalName, currentName, StringComparison.Ordinal) &&
                        dependents[principalName].Add(currentName))
                    {
                        inDegree[currentName]++;
                    }
                }
            }
        }

        var ready = new SortedSet<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key),
            StringComparer.Ordinal);

        var ordered = new List<(IEntityRegistry, DatabaseModel)>(registries.Count);

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            ordered.Add((registryByName[next], scannedSchemas[next]));

            foreach (var dep in dependents[next])
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0)
                {
                    ready.Add(dep);
                }
            }
        }

        if (ordered.Count < registries.Count)
        {
            var cyclic = string.Join(", ", inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key));
            throw new InvalidOperationException(
                $"Circular foreign key dependency detected among registries: {cyclic}. " +
                "Mark one side of the cycle with .IsCircular() in its ForeignKeyBuilder configuration.");
        }

        return ordered;
    }
}
