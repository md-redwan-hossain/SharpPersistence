using SharpPersistence.Enums;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DoctorEntityValidator(
    DbMigratorOptions options,
    ISqlDialectMetadata dialect)
{
    public void ValidateTable(
        TableDefinition tb,
        Dictionary<Type, (string RegistryName, TableDefinition Table)> allTables,
        IReadOnlyDictionary<string, PostgresEnumSchema> enumLookup,
        List<DoctorCheckItem> items)
    {
        var entityName = tb.EntityType.Name;
        ValidatePrimaryKey(tb, entityName, items);
        var resolvedSqlTypes = ValidateColumns(tb, entityName, enumLookup, items);
        ValidateIndexes(tb, entityName, items);
        ValidateCheckConstraints(tb, entityName, items);
        ValidateExclusionConstraints(tb, entityName, items);
        ValidateForeignKeys(tb, entityName, allTables, resolvedSqlTypes, enumLookup, items);
    }

    private static void ValidatePrimaryKey(TableDefinition tb, string entityName, List<DoctorCheckItem> items)
    {
        if (tb.PkProperties.Count == 0)
        {
            if (!tb.ExcludeFromMigrations)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Warning,
                    "No primary key configured and no 'Id' property found.",
                    Hint: "Call .HasKey(x => ...) if this table requires a primary key.",
                    EntityName: entityName));
            }

            return;
        }

        foreach (var pkProp in tb.PkProperties)
        {
            if (!tb.ColumnsByProp.ContainsKey(pkProp))
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Primary key references property '{pkProp}', which is ignored or not mapped as a column.",
                    EntityName: entityName));
            }
        }
    }

    private static Dictionary<string, string> ValidateColumns(
        TableDefinition tb,
        string entityName,
        IReadOnlyDictionary<string, PostgresEnumSchema> enumLookup,
        List<DoctorCheckItem> items)
    {
        var resolvedSqlTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var cb in tb.ColumnsByProp.Values)
        {
            if (cb.RequiresNullableColumn && !cb.IsNullable)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Column '{cb.PropertyName}' belongs to nullable owned navigation '{cb.OwnedNavigationPath}' and must be nullable.",
                    Hint: "Remove .IsRequired() on this owned property or make the navigation property non-nullable.",
                    EntityName: entityName));
            }

            try
            {
                resolvedSqlTypes[cb.PropertyName] = tb.ResolveColumnSqlType(cb, enumLookup);
            }
            catch (Exception ex)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Column '{cb.PropertyName}' cannot resolve a SQL type: {ex.Message}",
                    Hint: "Add .HasColumnType(...) or .HasConversion(...) for this property.",
                    EntityName: entityName));
            }

            if (cb.ComputedColumnSqlRenderer is not null)
            {
                try
                {
                    var sql = cb.ComputedColumnSqlRenderer(
                        tb.Dialect,
                        tb.IdentifierCase,
                        tb.SqlExpressionOptions.DelimitIdentifiers,
                        tb.SqlExpressionOptions.NestedColumnSeparator,
                        tb.ResolveMappedColumnName);
                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        items.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"Computed column '{cb.PropertyName}' produced empty SQL.",
                            EntityName: entityName));
                    }
                }
                catch (Exception ex)
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Computed column '{cb.PropertyName}' expression failed to render: {ex.Message}",
                        EntityName: entityName));
                }
            }
        }

        return resolvedSqlTypes;
    }

    private static void ValidateIndexes(TableDefinition tb, string entityName, List<DoctorCheckItem> items)
    {
        foreach (var ix in tb.Indexes)
        {
            foreach (var prop in ix.PropertyNames)
            {
                if (!tb.ColumnsByProp.ContainsKey(prop))
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Index '{ix.IndexName ?? string.Join("_", ix.PropertyNames)}' references unmapped or ignored property '{prop}'.",
                        EntityName: entityName));
                }
            }

            if (ix.IncludedPropertyNames is not null)
            {
                foreach (var incProp in ix.IncludedPropertyNames)
                {
                    if (!tb.ColumnsByProp.ContainsKey(incProp))
                    {
                        items.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"Index INCLUDE references unmapped or ignored property '{incProp}'.",
                            EntityName: entityName));
                    }
                }
            }

            if (ix.FilterRenderer is not null)
            {
                try
                {
                    _ = ix.FilterRenderer(
                        tb.Dialect,
                        tb.IdentifierCase,
                        tb.SqlExpressionOptions.DelimitIdentifiers,
                        tb.SqlExpressionOptions.NestedColumnSeparator,
                        tb.ResolveMappedColumnName);
                }
                catch (Exception ex)
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Index filter failed to render: {ex.Message}",
                        EntityName: entityName));
                }
            }
        }
    }

    private static void ValidateCheckConstraints(TableDefinition tb, string entityName, List<DoctorCheckItem> items)
    {
        foreach (var (ckName, ckDef) in tb.CheckConstraints)
        {
            try
            {
                var sql = ckDef.RenderSql(tb.Dialect, tb.IdentifierCase, tb.SqlExpressionOptions, tb.ResolveMappedColumnName);
                if (string.IsNullOrWhiteSpace(sql))
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Check constraint '{ckName}' produced empty SQL.",
                        EntityName: entityName));
                }
            }
            catch (Exception ex)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Check constraint '{ckName}' failed to render: {ex.Message}",
                    EntityName: entityName));
            }
        }
    }

    private void ValidateExclusionConstraints(TableDefinition tb, string entityName, List<DoctorCheckItem> items)
    {
        foreach (var (exName, exDef) in tb.ExclusionConstraints)
        {
            if (tb.Dialect != SqlDialect.PostgreSql)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Exclusion constraint '{exName}' is only supported on PostgreSQL (current dialect: {tb.Dialect}).",
                    EntityName: entityName));
                continue;
            }

            if (exDef.Elements.Count == 0)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Exclusion constraint '{exName}' must define at least one element via WithElement, WithRange, or WithExpression.",
                    EntityName: entityName));
                continue;
            }

            var hasBtreeGist = options.PostgreSql.Extensions.Any(e =>
                string.Equals(e.Name, "btree_gist", StringComparison.OrdinalIgnoreCase));

            foreach (var el in exDef.Elements)
            {
                if (el.PropertyName is not null)
                {
                    if (!tb.ColumnsByProp.TryGetValue(el.PropertyName, out var col))
                    {
                        items.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"Exclusion constraint '{exName}' references unmapped or ignored property '{el.PropertyName}'.",
                            EntityName: entityName));
                    }
                    else if (string.Equals(exDef.Method, "gist", StringComparison.OrdinalIgnoreCase) && !hasBtreeGist)
                    {
                        items.Add(new DoctorCheckItem(
                            DoctorSeverity.Warning,
                            $"Exclusion constraint '{exName}' uses GiST on scalar column '{col.ColumnName}', which usually requires the 'btree_gist' extension.",
                            Hint: "Add .HasPostgresExtension(\"btree_gist\") inside AddDbMigrator.",
                            EntityName: entityName));
                    }
                }
                else if (el.ExpressionRenderer is not null)
                {
                    try
                    {
                        _ = el.ExpressionRenderer(prop =>
                            tb.ColumnsByProp.TryGetValue(prop, out var col)
                                ? SqlFormatHelper.Quote(col.ColumnName)
                                : throw new InvalidOperationException($"Property '{prop}' is not mapped as a column."));
                    }
                    catch (Exception ex)
                    {
                        items.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"Exclusion constraint '{exName}' expression failed: {ex.Message}",
                            EntityName: entityName));
                    }
                }
                else if (el.SqlExpressionRenderer is not null)
                {
                    try
                    {
                        var sql = el.SqlExpressionRenderer(
                            tb.Dialect,
                            tb.IdentifierCase,
                            tb.SqlExpressionOptions.DelimitIdentifiers,
                            tb.SqlExpressionOptions.NestedColumnSeparator,
                            tb.ResolveMappedColumnName);
                        if (string.IsNullOrWhiteSpace(sql))
                        {
                            items.Add(new DoctorCheckItem(
                                DoctorSeverity.Error,
                                $"Exclusion constraint '{exName}' expression produced empty SQL.",
                                EntityName: entityName));
                        }
                    }
                    catch (Exception ex)
                    {
                        items.Add(new DoctorCheckItem(
                            DoctorSeverity.Error,
                            $"Exclusion constraint '{exName}' expression failed to render: {ex.Message}",
                            EntityName: entityName));
                    }
                }
            }

            if (exDef.FilterRenderer is not null)
            {
                try
                {
                    _ = exDef.FilterRenderer(
                        tb.Dialect,
                        tb.IdentifierCase,
                        tb.SqlExpressionOptions.DelimitIdentifiers,
                        tb.SqlExpressionOptions.NestedColumnSeparator,
                        tb.ResolveMappedColumnName);
                }
                catch (Exception ex)
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Exclusion constraint '{exName}' filter failed to render: {ex.Message}",
                        EntityName: entityName));
                }
            }
        }
    }

    private void ValidateForeignKeys(
        TableDefinition tb,
        string entityName,
        Dictionary<Type, (string RegistryName, TableDefinition Table)> allTables,
        Dictionary<string, string> resolvedSqlTypes,
        IReadOnlyDictionary<string, PostgresEnumSchema> enumLookup,
        List<DoctorCheckItem> items)
    {
        foreach (var fk in tb.ForeignKeys)
        {
            var resolvedPrincipalType = SchemaFinalizer.ResolveEntityReplacement(fk.PrincipalType, options.EntityReplacements);
            var fkDisplayName = fk.ConstraintName ?? $"{tb.TableName} -> {resolvedPrincipalType.Name}";
            if (!allTables.TryGetValue(resolvedPrincipalType, out var principalEntry))
            {
                var viaReplacement = resolvedPrincipalType != fk.PrincipalType
                    ? $" (resolved from replaced entity '{fk.PrincipalType.Name}')"
                    : string.Empty;
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Foreign key '{fkDisplayName}' references principal entity '{resolvedPrincipalType.Name}'{viaReplacement}, which is not registered in any IEntityRegistry.",
                    Hint: $"Map '{resolvedPrincipalType.Name}' in an IEntityRegistry or configure a replacement in ReplaceEntityTypes.",
                    EntityName: entityName));
                continue;
            }

            var principalTb = principalEntry.Table;
            if (fk.DependentProps.Count != fk.PrincipalProps.Count)
            {
                items.Add(new DoctorCheckItem(
                    DoctorSeverity.Error,
                    $"Foreign key '{fkDisplayName}' has {fk.DependentProps.Count} dependent column(s) but {fk.PrincipalProps.Count} principal column(s).",
                    EntityName: entityName));
                continue;
            }

            for (var i = 0; i < fk.DependentProps.Count; i++)
            {
                var depProp = fk.DependentProps[i];
                var prinProp = fk.PrincipalProps[i];

                if (!tb.ColumnsByProp.ContainsKey(depProp))
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Foreign key '{fkDisplayName}' references unmapped dependent property '{depProp}'.",
                        EntityName: entityName));
                    continue;
                }

                if (!principalTb.ColumnsByProp.TryGetValue(prinProp, out var prinCol))
                {
                    items.Add(new DoctorCheckItem(
                        DoctorSeverity.Error,
                        $"Foreign key '{fkDisplayName}' references unmapped principal property '{principalTb.EntityType.Name}.{prinProp}'.",
                        EntityName: entityName));
                    continue;
                }

                if (resolvedSqlTypes.TryGetValue(depProp, out var depSqlType))
                {
                    try
                    {
                        var prinSqlType = principalTb.ResolveColumnSqlType(prinCol, enumLookup);
                        if (!dialect.AreSqlTypesCompatible(depSqlType, prinSqlType))
                        {
                            items.Add(new DoctorCheckItem(
                                DoctorSeverity.Error,
                                $"Foreign key '{fkDisplayName}' references {principalTb.EntityType.Name}.{prinProp} ({prinSqlType}), but {entityName}.{depProp} is mapped as {depSqlType}.",
                                Hint: "Align the column types or add a value converter.",
                                EntityName: entityName));
                        }
                    }
                    catch
                    {
                        // Principal column error is already reported under the principal entity.
                    }
                }
            }
        }
    }
}
