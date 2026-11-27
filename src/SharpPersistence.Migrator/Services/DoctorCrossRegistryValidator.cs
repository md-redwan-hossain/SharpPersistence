using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DoctorCrossRegistryValidator(
    DbMigratorOptions options,
    ISqlDialectMetadata dialect)
{
    public void Validate(
        Dictionary<Type, (string RegistryName, TableDefinition Table)> allTables,
        List<DoctorCheckItem> checks,
        bool hasRegistryErrors)
    {
        var seenTables = new Dictionary<string, (string RegistryName, TableDefinition Table)>(StringComparer.Ordinal);
        var seenIndexes = new Dictionary<(string Schema, string IndexName), (string RegistryName, string TableName)>();
        var seenConstraints = new Dictionary<(string Schema, string ConstraintName), (string RegistryName, string TableName)>();
        var excludedTables = new List<(string RegistryName, TableDefinition Table, string NormalizedSchema, string QualifiedName)>();

        foreach (var (_, (regName, tb)) in allTables)
        {
            var normalizedSchema = dialect.NormalizeSchemaName(tb.Schema);
            var schemaLabel = string.IsNullOrEmpty(normalizedSchema) ? "(default)" : normalizedSchema;
            var displayQualifiedName = string.IsNullOrWhiteSpace(normalizedSchema)
                ? tb.TableName
                : $"{normalizedSchema}.{tb.TableName}";

            if (tb.ExcludeFromMigrations)
            {
                excludedTables.Add((regName, tb, normalizedSchema, displayQualifiedName));
                continue;
            }

            if (seenTables.TryGetValue(displayQualifiedName, out var existing))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Table ownership collision: '{displayQualifiedName}' is mapped by both {existing.RegistryName} ({existing.Table.EntityType.Name}) and {regName} ({tb.EntityType.Name}).",
                    Hint: "Use distinct table or schema names, or mark one as ExcludeFromMigrations()."));
            }
            else
            {
                seenTables[displayQualifiedName] = (regName, tb);
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

        var enumLookup = options.Enums.ToDictionary(e => e.ClrTypeName, StringComparer.Ordinal);
        foreach (var (regName, excludedTb, _, qualifiedName) in excludedTables)
        {
            if (!seenTables.TryGetValue(qualifiedName, out var owner))
            {
                continue;
            }

            var ownerCols = owner.Table.ColumnsByProp.Values
                .ToDictionary(c => c.ColumnName, c => c, StringComparer.Ordinal);

            foreach (var stubCol in excludedTb.ColumnsByProp.Values)
            {
                if (!ownerCols.TryGetValue(stubCol.ColumnName, out var ownerCol))
                {
                    checks.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"External ref mismatch: {regName}.{excludedTb.EntityType.Name} maps column '{stubCol.ColumnName}', which does not exist on {owner.RegistryName}.{owner.Table.EntityType.Name} ('{qualifiedName}').",
                        Hint: $"Update {excludedTb.EntityType.Name} configuration to match {owner.Table.EntityType.Name}."));
                    continue;
                }

                try
                {
                    var stubType = excludedTb.ResolveColumnSqlType(stubCol, enumLookup);
                    var ownerType = owner.Table.ResolveColumnSqlType(ownerCol, enumLookup);
                    if (!dialect.AreSqlTypesCompatible(stubType, ownerType))
                    {
                        checks.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"External ref mismatch: {regName}.{excludedTb.EntityType.Name} maps column '{stubCol.ColumnName}' as {stubType}, but {owner.RegistryName}.{owner.Table.EntityType.Name} defines {ownerType}.",
                            Hint: $"Update {excludedTb.EntityType.Name} configuration to match {owner.Table.EntityType.Name}."));
                    }
                }
                catch
                {
                    // Individual column type errors are already reported under entity checks.
                }
            }
        }

        foreach (var (sourceType, targetType) in options.EntityReplacements)
        {
            if (options.EntityReplacements.ContainsKey(targetType))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Invalid entity replacement: '{sourceType.Name}' is replaced with '{targetType.Name}', which is itself registered as a replaced entity.",
                    Hint: $"Replace '{sourceType.Name}' directly with the owning entity instead of chaining replacements."));
                continue;
            }

            if (!allTables.TryGetValue(targetType, out var targetEntry))
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Invalid entity replacement: '{sourceType.Name}' is replaced with '{targetType.Name}', which is not registered in any IEntityRegistry.",
                    Hint: $"Register '{targetType.Name}' in its owning IEntityRegistry."));
                continue;
            }

            if (targetEntry.Table.ExcludeFromMigrations)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Invalid entity replacement: '{sourceType.Name}' is replaced with '{targetType.Name}' in {targetEntry.RegistryName}, which is marked ExcludeFromMigrations().",
                    Hint: $"Replace '{sourceType.Name}' with the non-excluded owning entity instead."));
            }
        }

        if (!hasRegistryErrors && options.Registries.Count > 1)
        {
            try
            {
                _ = RegistryDependencyResolver.SortTopologically(options);
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
