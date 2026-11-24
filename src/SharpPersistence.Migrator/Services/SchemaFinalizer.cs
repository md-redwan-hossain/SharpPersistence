using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class SchemaFinalizer
{
    public static DatabaseModel FinalizeSchema(
        Dictionary<Type, TableDefinition> builders,
        IReadOnlySet<Type>? includedEntityTypes,
        IReadOnlyList<SequenceSchema>? sequences = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null,
        IReadOnlyDictionary<Type, Type>? entityStubs = null)
    {
        var enumLookup = (pgEnums ?? []).ToDictionary(e => e.ClrTypeName, StringComparer.Ordinal);
        var schema = new DatabaseModel
        {
            Sequences = sequences is { Count: > 0 }
                ? sequences.OrderBy(s => s.QualifiedName, StringComparer.Ordinal).ToList()
                : null
        };
        var seenQualifiedNames = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var (clrType, tb) in builders)
        {
            var tableSchemaName = string.IsNullOrWhiteSpace(tb.Schema) ? null : tb.Schema;
            if (!tb.ExcludeFromMigrations)
            {
                var qualifiedName = string.IsNullOrWhiteSpace(tableSchemaName)
                    ? tb.TableName
                    : $"{tableSchemaName}.{tb.TableName}";

                if (seenQualifiedNames.TryGetValue(qualifiedName, out var existingType))
                {
                    throw new InvalidOperationException(
                        $"Duplicate table '{qualifiedName}' mapped by both '{existingType.Name}' and '{clrType.Name}'.");
                }

                seenQualifiedNames[qualifiedName] = clrType;
            }

            if (includedEntityTypes is not null && !includedEntityTypes.Contains(clrType))
            {
                continue;
            }

            var table = CreateBaseTableSchema(clrType, tb, tableSchemaName);
            BuildColumns(table, tb, enumLookup);
            BuildPrimaryKey(table, tb);
            BuildIndexes(table, tb);
            BuildCheckConstraints(table, tb);
            BuildExclusionConstraints(table, tb);
            BuildForeignKeys(table, tb, builders, entityStubs);

            table.Indexes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            table.ForeignKeys.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            schema.Tables.Add(table);
            schema.ByClrType[clrType] = table;
        }

        schema.Tables.Sort((a, b) => string.CompareOrdinal(a.QualifiedName, b.QualifiedName));
        return schema;
    }

    private static TableSchema CreateBaseTableSchema(Type clrType, TableDefinition tb, string? tableSchemaName) =>
        new()
        {
            EntityName = clrType.FullName ?? clrType.Name,
            TableName = tb.TableName,
            Schema = tableSchemaName,
            PreviousTableName = tb.PreviousTableName,
            PreviousSchema = tb.PreviousSchema,
            Comment = tb.Comment,
            ExcludeFromMigrations = tb.ExcludeFromMigrations,
            IsUnlogged = tb.IsUnlogged,
            Tablespace = tb.Tablespace,
            StorageParameters = tb.StorageParameters is { Count: > 0 }
                ? tb.StorageParameters
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                : null,
            QueryFilter = tb.QueryFilter,
            NamedQueryFilters = new Dictionary<string, System.Linq.Expressions.LambdaExpression?>(tb.NamedQueryFilters, StringComparer.Ordinal),
            Annotations = new Dictionary<string, object?>(tb.Annotations, StringComparer.Ordinal),
            IgnoredProperties = [.. tb.IgnoredProperties]
        };

    private static void BuildColumns(
        TableSchema table,
        TableDefinition tb,
        IReadOnlyDictionary<string, PostgresEnumSchema> enumLookup)
    {
        foreach (var cb in tb.ColumnsByProp.Values.OrderBy(x => x.ColumnName, StringComparer.Ordinal))
        {
            var isPgXmin = tb.IsPostgresXmin(cb);

            table.Columns.Add(new ColumnSchema
            {
                PropertyName = cb.PropertyName,
                ColumnName = isPgXmin ? "xmin" : cb.ColumnName,
                PreviousColumnName = cb.PreviousColumnName,
                ClrTypeName = FormatCleanClrTypeName(cb.ClrType),
                SqlType = tb.ResolveColumnSqlType(cb, enumLookup),
                IsNullable = !isPgXmin && cb.IsNullable,
                MaxLength = cb.MaxLength,
                Precision = cb.Precision,
                Scale = cb.Scale,
                IsUnicode = cb.IsUnicode,
                DefaultValueSql = cb.DefaultValueSql,
                IsIdentity = cb.IsIdentity,
                ValueGenerated = cb.ValueGenerated,
                ComputedColumnSql = cb.ComputedColumnSqlRenderer is not null
                    ? cb.ComputedColumnSqlRenderer(
                        tb.Dialect,
                        tb.IdentifierCase,
                        tb.SqlExpressionOptions.DelimitIdentifiers,
                        tb.SqlExpressionOptions.NestedColumnSeparator,
                        tb.ResolveMappedColumnName)
                    : cb.ComputedColumnSql,
                IsComputedStored = cb.IsComputedStored,
                Comment = cb.Comment,
                IsConcurrencyToken = cb.IsConcurrencyToken,
                IsRowVersion = cb.IsRowVersion,
                IsSystemColumn = isPgXmin || cb.IsSystemColumn,
                ProviderClrType = cb.ProviderClrType,
                ValueConverter = cb.ValueConverter,
                Annotations = new Dictionary<string, object?>(cb.Annotations, StringComparer.Ordinal)
            });
        }
    }

    private static void BuildPrimaryKey(TableSchema table, TableDefinition tb)
    {
        if (tb.PkProperties.Count == 0)
        {
            return;
        }

        var pkCols = tb.PkProperties
            .Select(p => ResolveColumnName(tb, p, "Primary Key"))
            .ToList();

        var pkPrefix = NamingFormatter.FormatPrefix("pk", tb.IdentifierCase);
        table.PrimaryKey = new PrimaryKeySchema
        {
            Name = NamingFormatter.TruncateIdentifier(tb.PkName ?? $"{pkPrefix}_{tb.TableName}", tb.Dialect),
            ColumnNames = pkCols,
            PropertyNames = [.. tb.PkProperties]
        };
    }

    private static void BuildIndexes(TableSchema table, TableDefinition tb)
    {
        foreach (var ib in tb.Indexes)
        {
            var ixCols = ib.PropertyNames
                .Select(p => ResolveColumnName(tb, p, "Index"))
                .ToList();

            var incCols = ib.IncludedPropertyNames?
                .Select(p => ResolveColumnName(tb, p, "Index Include"))
                .ToList();

            Dictionary<string, string>? operatorsByCol = null;
            Dictionary<string, string>? operatorsByProp = null;
            if (ib.OperatorsByProperty is { Count: > 0 })
            {
                operatorsByCol = new Dictionary<string, string>(StringComparer.Ordinal);
                operatorsByProp = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (propName, opClass) in ib.OperatorsByProperty.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    var colName = ResolveColumnName(tb, propName, "Index Operator");
                    operatorsByCol[colName] = opClass;
                    operatorsByProp[propName] = opClass;
                }
            }

            var filterSql = ib.FilterRenderer is not null
                ? ib.FilterRenderer(
                    tb.Dialect,
                    tb.IdentifierCase,
                    tb.SqlExpressionOptions.DelimitIdentifiers,
                    tb.SqlExpressionOptions.NestedColumnSeparator,
                    tb.ResolveMappedColumnName)
                : ib.FilterSql;

            table.Indexes.Add(new IndexSchema
            {
                Name = tb.ResolveIndexName(ib, ixCols),
                ColumnNames = ixCols,
                PropertyNames = [.. ib.PropertyNames],
                IsUnique = ib.IsUnique,
                FilterSql = filterSql,
                IncludedColumnNames = incCols is { Count: > 0 } ? incCols : null,
                IncludedPropertyNames = ib.IncludedPropertyNames is { Count: > 0 } ? [.. ib.IncludedPropertyNames] : null,
                IsDescending = ib.IsDescending is { Count: > 0 } ? [.. ib.IsDescending] : null,
                Method = ib.Method,
                Tablespace = ib.Tablespace,
                StorageParameters = ib.StorageParameters is { Count: > 0 }
                    ? ib.StorageParameters
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    : null,
                OperatorsByColumn = operatorsByCol,
                OperatorsByProperty = operatorsByProp
            });
        }
    }

    private static void BuildCheckConstraints(TableSchema table, TableDefinition tb)
    {
        foreach (var (ckName, ckDef) in tb.CheckConstraints.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            table.CheckConstraints.Add(new CheckConstraintSchema
            {
                Name = NamingFormatter.TruncateIdentifier(ckName, tb.Dialect),
                Sql = ckDef.RenderSql(tb.Dialect, tb.IdentifierCase, tb.SqlExpressionOptions, tb.ResolveMappedColumnName)
            });
        }
    }

    private static void BuildExclusionConstraints(TableSchema table, TableDefinition tb)
    {
        if (tb.ExclusionConstraints.Count == 0)
        {
            return;
        }

        table.ExclusionConstraints = [];
        foreach (var (exName, exDef) in tb.ExclusionConstraints.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (exDef.Elements.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Exclusion constraint '{exName}' on entity '{tb.EntityType.Name}' must define at least one element via WithElement or WithExpression.");
            }

            var elements = new List<ExclusionConstraintElementSchema>(exDef.Elements.Count);
            foreach (var el in exDef.Elements)
            {
                if (el.PropertyName is not null)
                {
                    var colName = ResolveColumnName(tb, el.PropertyName, "Exclusion Constraint");
                    elements.Add(new ExclusionConstraintElementSchema
                    {
                        ColumnName = colName,
                        PropertyName = el.PropertyName,
                        OperatorClass = el.OperatorClass,
                        Operator = el.Operator
                    });
                }
                else if (el.SqlExpressionRenderer is not null)
                {
                    var exprSql = el.SqlExpressionRenderer(
                        tb.Dialect,
                        tb.IdentifierCase,
                        tb.SqlExpressionOptions.DelimitIdentifiers,
                        tb.SqlExpressionOptions.NestedColumnSeparator,
                        tb.ResolveMappedColumnName);
                    elements.Add(new ExclusionConstraintElementSchema
                    {
                        ExpressionSql = exprSql,
                        Operator = el.Operator
                    });
                }
                else if (el.ExpressionRenderer is not null)
                {
                    var exprSql = el.ExpressionRenderer(prop =>
                        SqlFormatHelper.Quote(ResolveColumnName(tb, prop, "Exclusion Constraint")));
                    elements.Add(new ExclusionConstraintElementSchema
                    {
                        ExpressionSql = exprSql,
                        Operator = el.Operator
                    });
                }
                else
                {
                    elements.Add(new ExclusionConstraintElementSchema
                    {
                        ExpressionSql = el.ExpressionSql,
                        Operator = el.Operator
                    });
                }
            }

            var incCols = exDef.IncludedPropertyNames?
                .Select(p => ResolveColumnName(tb, p, "Exclusion Constraint Include"))
                .ToList();

            var filterSql = exDef.FilterRenderer is not null
                ? exDef.FilterRenderer(
                    tb.Dialect,
                    tb.IdentifierCase,
                    tb.SqlExpressionOptions.DelimitIdentifiers,
                    tb.SqlExpressionOptions.NestedColumnSeparator,
                    tb.ResolveMappedColumnName)
                : exDef.FilterSql;

            table.ExclusionConstraints.Add(new ExclusionConstraintSchema
            {
                Name = NamingFormatter.TruncateIdentifier(exName, tb.Dialect),
                Method = exDef.Method,
                Elements = elements,
                IncludedColumnNames = incCols is { Count: > 0 } ? incCols : null,
                IncludedPropertyNames = exDef.IncludedPropertyNames is { Count: > 0 } ? [.. exDef.IncludedPropertyNames] : null,
                StorageParameters = exDef.StorageParameters is { Count: > 0 }
                    ? exDef.StorageParameters
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    : null,
                Tablespace = exDef.Tablespace,
                FilterSql = filterSql
            });
        }
    }

    internal static Type ResolveEntityStub(
        Type principalType,
        IReadOnlyDictionary<Type, Type>? entityStubs)
    {
        if (entityStubs is { Count: > 0 } && entityStubs.TryGetValue(principalType, out var target))
        {
            return target;
        }

        return principalType;
    }

    private static void BuildForeignKeys(
        TableSchema table,
        TableDefinition tb,
        Dictionary<Type, TableDefinition> builders,
        IReadOnlyDictionary<Type, Type>? entityStubs)
    {
        foreach (var fk in tb.ForeignKeys)
        {
            var resolvedPrincipalType = ResolveEntityStub(fk.PrincipalType, entityStubs);
            if (!builders.TryGetValue(resolvedPrincipalType, out var principalTb))
            {
                var viaStub = resolvedPrincipalType != fk.PrincipalType
                    ? $" (resolved from stub '{fk.PrincipalType.Name}')"
                    : string.Empty;
                throw new InvalidOperationException(
                    $"Principal entity '{resolvedPrincipalType.Name}'{viaStub} is not registered in any IEntityRegistry.");
            }

            var depCols = fk.DependentProps
                .Select(p => ResolveColumnName(tb, p, "Foreign Key"))
                .ToList();
            var prinCols = fk.PrincipalProps
                .Select(p => ResolveColumnName(principalTb, p, "Foreign Key Principal"))
                .ToList();

            var fkPrefix = NamingFormatter.FormatPrefix("fk", tb.IdentifierCase);
            var fkName = NamingFormatter.TruncateIdentifier(
                fk.ConstraintName ?? $"{fkPrefix}_{tb.TableName}_{principalTb.TableName}_{string.Join("_", depCols)}",
                tb.Dialect);

            table.ForeignKeys.Add(new ForeignKeySchema
            {
                Name = fkName,
                DependentColumns = depCols,
                DependentProperties = [.. fk.DependentProps],
                PrincipalEntityName = resolvedPrincipalType.FullName ?? resolvedPrincipalType.Name,
                PrincipalSchema = string.IsNullOrWhiteSpace(principalTb.Schema) ? null : principalTb.Schema,
                PrincipalTable = principalTb.TableName,
                PrincipalColumns = prinCols,
                PrincipalProperties = [.. fk.PrincipalProps],
                OnDelete = fk.OnDelete,
                IsCircular = fk.IsCircular
            });

            if (fk.IsUnique)
            {
                var isPrimaryKey = table.PrimaryKey is not null &&
                                   table.PrimaryKey.PropertyNames.SequenceEqual(fk.DependentProps, StringComparer.Ordinal);
                var hasUniqueIndex = table.Indexes.Any(ix =>
                    ix.IsUnique &&
                    ix.PropertyNames.SequenceEqual(fk.DependentProps, StringComparer.Ordinal));

                if (!isPrimaryKey && !hasUniqueIndex)
                {
                    var uqPrefix = NamingFormatter.FormatPrefix("uq", tb.IdentifierCase);
                    table.Indexes.Add(new IndexSchema
                    {
                        Name = NamingFormatter.TruncateIdentifier(
                            $"{uqPrefix}_{tb.TableName}_{string.Join("_", depCols)}",
                            tb.Dialect),
                        ColumnNames = depCols,
                        PropertyNames = [.. fk.DependentProps],
                        IsUnique = true
                    });
                }
            }
        }
    }

    private static string ResolveColumnName(TableDefinition table, string propertyName, string context)
    {
        if (table.ColumnsByProp.TryGetValue(propertyName, out var col))
        {
            return col.ColumnName;
        }

        throw new InvalidOperationException(
            $"{context} on entity '{table.EntityType.Name}' references property '{propertyName}', " +
            "which is either ignored or not mapped as a scalar column.");
    }

    private static string FormatCleanClrTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var coreType = underlying ?? type;

        var alias = coreType switch
        {
            _ when coreType == typeof(bool) => "bool",
            _ when coreType == typeof(byte) => "byte",
            _ when coreType == typeof(short) => "short",
            _ when coreType == typeof(int) => "int",
            _ when coreType == typeof(uint) => "uint",
            _ when coreType == typeof(long) => "long",
            _ when coreType == typeof(float) => "float",
            _ when coreType == typeof(double) => "double",
            _ when coreType == typeof(decimal) => "decimal",
            _ when coreType == typeof(string) => "string",
            _ when coreType == typeof(Guid) => "Guid",
            _ when coreType == typeof(DateTime) => "DateTime",
            _ when coreType == typeof(DateTimeOffset) => "DateTimeOffset",
            _ when coreType == typeof(DateOnly) => "DateOnly",
            _ when coreType == typeof(TimeOnly) => "TimeOnly",
            _ when coreType == typeof(TimeSpan) => "TimeSpan",
            _ when coreType == typeof(byte[]) => "byte[]",
            _ => coreType.Name
        };

        return underlying is not null ? $"{alias}?" : alias;
    }
}
