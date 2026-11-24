using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class RegistryDependencyResolver
{
    public static IReadOnlyList<IEntityRegistry> SortTopologically(MigrationManagerOptions options) =>
        SortTopologically(
            options.Registries,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityStubs,
            options.Enums,
            options.SqlExpressionOptions);

    public static IReadOnlyList<IEntityRegistry> SortTopologically(
        IReadOnlyList<IEntityRegistry> registries,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null,
        bool pluralizeTableNames = false,
        IReadOnlyList<EntityConventionRule>? entityConventions = null,
        Func<Type, string, string>? tableNameConvention = null,
        IReadOnlyDictionary<Type, Type>? entityStubs = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        SqlExpressionBuilderOptions? sqlExpressionOptions = null)
    {
        if (registries.Count <= 1)
        {
            return registries;
        }

        var dialectMeta = MigrationDialectRegistry.Get(dialect);
        var registryByName = registries.ToDictionary(r => r.GetType().Name, StringComparer.Ordinal);
        var ownerByEntityName = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownerByQualifiedTable = new Dictionary<(string Schema, string Table), string>();
        var excludedEntities = new HashSet<string>(StringComparer.Ordinal);
        var scannedSchemas = new Dictionary<string, DatabaseModel>(StringComparer.Ordinal);

        foreach (var registry in registries)
        {
            var regName = registry.GetType().Name;
            var evaluated = EntityRegistryBuilder.Evaluate(registry);
            foreach (var entityType in evaluated.Mappings.Keys)
            {
                var fullName = entityType.FullName ?? entityType.Name;
                ownerByEntityName[fullName] = regName;
            }

            var schema = SchemaScanner.ScanRegistry(
                registry,
                dialect,
                identifierCase,
                registries,
                propertyConventions,
                pgEnums: pgEnums,
                sqlExpressionOptions: sqlExpressionOptions,
                pluralizeTableNames: pluralizeTableNames,
                entityConventions: entityConventions,
                tableNameConvention: tableNameConvention,
                entityStubs: entityStubs);

            scannedSchemas[regName] = schema;

            foreach (var table in schema.Tables)
            {
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

        var ordered = new List<IEntityRegistry>(registries.Count);

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            ordered.Add(registryByName[next]);

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
