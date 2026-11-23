using SharpPersistence.Enums;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class SchemaFinalizer
{
    public static DatabaseSchema FinalizeSchema(
        Dictionary<Type, TableDefinition> builders,
        IReadOnlySet<Type>? includedEntityTypes,
        IReadOnlyList<SequenceSchema>? sequences = null,
        IReadOnlyList<PostgresEnumSchema>? pgEnums = null)
    {
        var enumLookup = (pgEnums ?? []).ToDictionary(e => e.ClrTypeName, StringComparer.Ordinal);
        var schema = new DatabaseSchema
        {
            Sequences = sequences is { Count: > 0 }
                ? sequences.OrderBy(s => s.QualifiedName, StringComparer.Ordinal).ToList()
                : null
        };
        var seenQualifiedNames = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var (clrType, tb) in builders)
        {
            var tableSchemaName = string.IsNullOrWhiteSpace(tb.Schema) ? null : tb.Schema;
            var qualifiedName = string.IsNullOrWhiteSpace(tableSchemaName)
                ? tb.TableName
                : $"{tableSchemaName}.{tb.TableName}";

            if (seenQualifiedNames.TryGetValue(qualifiedName, out var existingType))
            {
                throw new InvalidOperationException(
                    $"Duplicate table '{qualifiedName}' mapped by both '{existingType.Name}' and '{clrType.Name}'.");
            }

            seenQualifiedNames[qualifiedName] = clrType;

            if (includedEntityTypes is not null && !includedEntityTypes.Contains(clrType))
            {
                continue;
            }

            var convention = tb.IdentifierCase;
            var table = new TableSchema
            {
                EntityName = clrType.FullName ?? clrType.Name,
                TableName = tb.TableName,
                Schema = tableSchemaName,
                PreviousTableName = tb.PreviousTableName,
                PreviousSchema = tb.PreviousSchema,
                Comment = tb.Comment,
                IsUnlogged = tb.IsUnlogged,
                Tablespace = tb.Tablespace,
                StorageParameters = tb.StorageParameters is { Count: > 0 }
                    ? tb.StorageParameters
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    : null,
                QueryFilter = tb.QueryFilter,
                NamedQueryFilters = new Dictionary<string, System.Linq.Expressions.LambdaExpression?>(tb.NamedQueryFilters, StringComparer.Ordinal),
                IgnoredProperties = [.. tb.IgnoredProperties]
            };

            foreach (var cb in tb.ColumnsByProp.Values.OrderBy(x => x.ColumnName, StringComparer.Ordinal))
            {
                var unwrapped = Nullable.GetUnderlyingType(cb.ClrType) ?? cb.ClrType;
                var isPgXmin = tb.Dialect == SqlDialect.PostgreSql && cb.IsRowVersion && unwrapped == typeof(uint);

                table.Columns.Add(new ColumnSchema
                {
                    PropertyName = cb.PropertyName,
                    ColumnName = isPgXmin ? "xmin" : cb.ColumnName,
                    PreviousColumnName = cb.PreviousColumnName,
                    ClrTypeName = FormatCleanClrTypeName(cb.ClrType),
                    SqlType = isPgXmin
                        ? "xid"
                        : SqlTypeResolver.Resolve(
                            tb.Dialect,
                            cb.ProviderClrType ?? cb.ClrType,
                            cb.ExplicitSqlType,
                            cb.MaxLength,
                            cb.Precision,
                            cb.Scale,
                            cb.IsUnicode,
                            enumLookup),
                    IsNullable = isPgXmin ? false : cb.IsNullable,
                    MaxLength = cb.MaxLength,
                    Precision = cb.Precision,
                    Scale = cb.Scale,
                    IsUnicode = cb.IsUnicode,
                    DefaultValueSql = cb.DefaultValueSql,
                    IsIdentity = cb.IsIdentity,
                    ValueGenerated = cb.ValueGenerated,
                    ComputedColumnSql = cb.ComputedColumnSql,
                    IsComputedStored = cb.IsComputedStored,
                    Comment = cb.Comment,
                    IsConcurrencyToken = cb.IsConcurrencyToken,
                    IsRowVersion = cb.IsRowVersion,
                    IsSystemColumn = isPgXmin || cb.IsSystemColumn,
                    ProviderClrType = cb.ProviderClrType,
                    ValueConverter = cb.ValueConverter
                });
            }

            if (tb.PkProperties.Count > 0)
            {
                var pkCols = tb.PkProperties
                    .Select(p => ResolveColumnName(tb, p, "Primary Key"))
                    .ToList();

                var pkPrefix = NamingFormatter.FormatPrefix("pk", convention);
                table.PrimaryKey = new PrimaryKeySchema
                {
                    Name = NamingFormatter.TruncateIdentifier(tb.PkName ?? $"{pkPrefix}_{tb.TableName}", tb.Dialect),
                    ColumnNames = pkCols,
                    PropertyNames = [.. tb.PkProperties]
                };
            }

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

                var ixPrefix = NamingFormatter.FormatPrefix(ib.IsUnique ? "uq" : "ix", convention);
                var ixName = NamingFormatter.TruncateIdentifier(
                    ib.IndexName ?? $"{ixPrefix}_{tb.TableName}_{string.Join("_", ixCols)}",
                    tb.Dialect);
                table.Indexes.Add(new IndexSchema
                {
                    Name = ixName,
                    ColumnNames = ixCols,
                    PropertyNames = [.. ib.PropertyNames],
                    IsUnique = ib.IsUnique,
                    FilterSql = ib.FilterSql,
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

            foreach (var (ckName, ckDef) in tb.CheckConstraints.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                table.CheckConstraints.Add(new CheckConstraintSchema
                {
                    Name = NamingFormatter.TruncateIdentifier(ckName, tb.Dialect),
                    Sql = ckDef.RenderSql(tb.Dialect, tb.IdentifierCase, tb.CheckConstraintOptions)
                });
            }

            foreach (var fk in tb.ForeignKeys)
            {
                if (!builders.TryGetValue(fk.PrincipalType, out var principalTb))
                {
                    throw new InvalidOperationException(
                        $"Principal entity '{fk.PrincipalType.Name}' is not registered in any IEntityRegistry.");
                }

                var depCols = fk.DependentProps
                    .Select(p => ResolveColumnName(tb, p, "Foreign Key"))
                    .ToList();
                var prinCols = fk.PrincipalProps
                    .Select(p => ResolveColumnName(principalTb, p, "Foreign Key Principal"))
                    .ToList();

                var fkPrefix = NamingFormatter.FormatPrefix("fk", convention);
                var fkName = NamingFormatter.TruncateIdentifier(
                    fk.ConstraintName ?? $"{fkPrefix}_{tb.TableName}_{principalTb.TableName}_{string.Join("_", depCols)}",
                    tb.Dialect);

                table.ForeignKeys.Add(new ForeignKeySchema
                {
                    Name = fkName,
                    DependentColumns = depCols,
                    DependentProperties = [.. fk.DependentProps],
                    PrincipalEntityName = fk.PrincipalType.FullName ?? fk.PrincipalType.Name,
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
                        var uqPrefix = NamingFormatter.FormatPrefix("uq", convention);
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

            table.Indexes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            table.ForeignKeys.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            schema.Tables.Add(table);
            schema.ByClrType[clrType] = table;
        }

        schema.Tables.Sort((a, b) => string.CompareOrdinal(a.QualifiedName, b.QualifiedName));
        return schema;
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
            _ when coreType == typeof(byte[]) => "byte[]",
            _ => coreType.Name
        };

        return underlying is not null ? $"{alias}?" : alias;
    }
}
