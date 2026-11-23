using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;

namespace SharpPersistence.Migrator.Services;

public static class RegistryDependencyResolver
{
    public static IReadOnlyList<IEntityRegistry> SortTopologically(
        IReadOnlyList<IEntityRegistry> registries,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        IReadOnlyList<PropertyConventionRule>? propertyConventions = null)
    {
        if (registries.Count <= 1)
        {
            return registries;
        }

        var registryByName = registries.ToDictionary(r => r.GetType().Name, StringComparer.Ordinal);
        var ownerByEntityName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var registry in registries)
        {
            var regName = registry.GetType().Name;
            var evaluated = EntityRegistryBuilder.Evaluate(registry);
            foreach (var entityType in evaluated.Mappings.Keys)
            {
                var fullName = entityType.FullName ?? entityType.Name;
                ownerByEntityName[fullName] = regName;
            }
        }

        var dependents = registryByName.Keys.ToDictionary(
            k => k,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var inDegree = registryByName.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);

        foreach (var registry in registries)
        {
            var currentName = registry.GetType().Name;
            var schema = SchemaScanner.ScanRegistry(
                registry,
                dialect,
                identifierCase,
                registries,
                propertyConventions);

            foreach (var table in schema.Tables)
            {
                foreach (var fk in table.ForeignKeys)
                {
                    if (fk.IsCircular || string.IsNullOrWhiteSpace(fk.PrincipalEntityName))
                    {
                        continue;
                    }

                    if (ownerByEntityName.TryGetValue(fk.PrincipalEntityName, out var principalName) &&
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
