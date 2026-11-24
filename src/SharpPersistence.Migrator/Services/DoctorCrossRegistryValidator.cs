using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DoctorCrossRegistryValidator(
    MigrationManagerOptions options,
    IMigrationDialect dialect)
{
    public void Validate(
        Dictionary<Type, (string RegistryName, TableDefinition Table)> allTables,
        List<DoctorCheckItem> checks,
        bool hasRegistryErrors)
    {
        var seenTables = new Dictionary<string, (string RegistryName, string EntityName)>(StringComparer.Ordinal);
        var seenIndexes = new Dictionary<(string Schema, string IndexName), (string RegistryName, string TableName)>();
        var seenConstraints = new Dictionary<(string Schema, string ConstraintName), (string RegistryName, string TableName)>();

        foreach (var (_, (regName, tb)) in allTables)
        {
            var normalizedSchema = dialect.NormalizeSchemaName(tb.Schema);
            var schemaLabel = string.IsNullOrEmpty(normalizedSchema) ? "(default)" : normalizedSchema;
            var displayQualifiedName = string.IsNullOrWhiteSpace(normalizedSchema)
                ? tb.TableName
                : $"{normalizedSchema}.{tb.TableName}";

            if (seenTables.TryGetValue(displayQualifiedName, out var existing))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Table ownership collision: '{displayQualifiedName}' is mapped by both {existing.RegistryName} ({existing.EntityName}) and {regName} ({tb.EntityType.Name}).",
                    Hint: "Use distinct table or schema names."));
            }
            else
            {
                seenTables[displayQualifiedName] = (regName, tb.EntityType.Name);
            }

            foreach (var ix in tb.Indexes)
            {
                var ixName = tb.ResolveIndexName(ix);
                var ixKey = (normalizedSchema, ixName);
                if (seenIndexes.TryGetValue(ixKey, out var existingIx))
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Duplicate index name '{ixName}' in schema '{schemaLabel}' on both {existingIx.RegistryName}.{existingIx.TableName} and {regName}.{tb.TableName}.",
                        Hint: "Use .HasDatabaseName(...) to give each index a unique name."));
                }
                else
                {
                    seenIndexes[ixKey] = (regName, tb.TableName);
                }
            }

            void TrackConstraint(string constraintName)
            {
                var key = (normalizedSchema, constraintName);
                if (seenConstraints.TryGetValue(key, out var existingConstraint))
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Duplicate constraint name '{constraintName}' in schema '{schemaLabel}' on both {existingConstraint.RegistryName}.{existingConstraint.TableName} and {regName}.{tb.TableName}.",
                        Hint: "Give each constraint a unique name."));
                }
                else
                {
                    seenConstraints[key] = (regName, tb.TableName);
                }
            }

            foreach (var fk in tb.ForeignKeys)
            {
                if (!string.IsNullOrWhiteSpace(fk.ConstraintName))
                {
                    TrackConstraint(NamingFormatter.TruncateIdentifier(fk.ConstraintName, tb.Dialect));
                }
            }

            foreach (var ckName in tb.CheckConstraints.Keys)
            {
                TrackConstraint(NamingFormatter.TruncateIdentifier(ckName, tb.Dialect));
            }

            foreach (var exName in tb.ExclusionConstraints.Keys)
            {
                TrackConstraint(NamingFormatter.TruncateIdentifier(exName, tb.Dialect));
            }
        }

        if (!hasRegistryErrors && options.Registries.Count > 1)
        {
            try
            {
                _ = RegistryDependencyResolver.SortTopologically(
                    options.Registries,
                    options.Dialect,
                    options.IdentifierCase,
                    options.PropertyConventions);
            }
            catch (Exception ex)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    ex.Message,
                    Hint: "Mark one side of the cycle with .IsCircular() on its foreign key configuration."));
            }
        }
    }
}
