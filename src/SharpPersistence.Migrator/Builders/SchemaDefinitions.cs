using System.Linq.Expressions;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

internal delegate string SqlExpressionRenderDelegate(
    SqlDialect dialect,
    SqlIdentifierCase identifierCase,
    bool delimitIdentifiers,
    string nestedSeparator,
    Func<string, string?> resolveColumnName);

internal static class SqlExpressionBuilderFactory
{
    public static SqlExpressionBuilder<TEntity> Create<TEntity>(
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        bool delimitIdentifiers,
        string nestedSeparator,
        Func<string, string?> resolveColumnName) =>
        new()
        {
            Dialect = dialect,
            IdentifierCase = identifierCase,
            DelimitIdentifiers = delimitIdentifiers,
            NestedSeparator = nestedSeparator,
            ColumnNameResolver = resolveColumnName
        };
}

internal sealed class TableDefinition
{
    public Type EntityType { get; }
    public string TableName { get; set; }
    public string? Schema { get; set; }
    public string? PreviousTableName { get; set; }
    public string? PreviousSchema { get; set; }
    public string? Comment { get; set; }
    public bool ExcludeFromMigrations { get; set; }
    public bool IsUnlogged { get; set; }
    public string? Tablespace { get; set; }
    public Dictionary<string, string>? StorageParameters { get; set; }
    public Dictionary<string, object?> Annotations { get; } = new(StringComparer.Ordinal);
    public LambdaExpression? QueryFilter { get; set; }
    public Dictionary<string, LambdaExpression?> NamedQueryFilters { get; } = new(StringComparer.Ordinal);
    public HashSet<string> IgnoredProperties { get; } = new(StringComparer.Ordinal);
    public SqlDialect Dialect { get; }
    public SqlIdentifierCase IdentifierCase { get; }
    public SqlExpressionBuilderOptions SqlExpressionOptions { get; }
    public IReadOnlyList<string> PkProperties { get; private set; } = [];
    public string? PkName { get; private set; }
    public Dictionary<string, ColumnDefinition> ColumnsByProp { get; } = new(StringComparer.Ordinal);
    public List<IndexDefinition> Indexes { get; } = [];
    public Dictionary<string, CheckConstraintDefinition> CheckConstraints { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ExclusionConstraintDefinition> ExclusionConstraints { get; } = new(StringComparer.Ordinal);
    public List<ForeignKeyDefinition> ForeignKeys { get; } = [];
    private bool _isApplyingEntityConventions;

    public IDisposable BeginConventionScope()
    {
        _isApplyingEntityConventions = true;
        return new ConventionScope(this);
    }

    private sealed class ConventionScope(TableDefinition owner) : IDisposable
    {
        public void Dispose() => owner._isApplyingEntityConventions = false;
    }

    public TableDefinition(
        Type entityType,
        string tableName,
        string? schema,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        SqlExpressionBuilderOptions? sqlExpressionOptions = null)
    {
        EntityType = entityType;
        TableName = tableName;
        Schema = schema;
        Dialect = dialect;
        IdentifierCase = identifierCase;
        SqlExpressionOptions = sqlExpressionOptions ?? new SqlExpressionBuilderOptions();
    }

    public void SetPrimaryKey(IReadOnlyList<string> propNames, string? constraintName = null)
    {
        PkProperties = propNames;
        PkName = constraintName;
    }

    public void SetPrimaryKeyName(string constraintName)
    {
        PkName = constraintName;
    }

    public ColumnDefinition GetOrAddColumn(string propName, Type clrType)
    {
        if (!ColumnsByProp.TryGetValue(propName, out var col))
        {
            col = new ColumnDefinition(propName, clrType, NamingFormatter.Format(propName, IdentifierCase));
            ColumnsByProp[propName] = col;
        }

        return col;
    }

    public void IgnoreProperty(string propName)
    {
        IgnoredProperties.Add(propName);
        ColumnsByProp.Remove(propName);
    }

    public string? ResolveMappedColumnName(string propertyName) =>
        ColumnsByProp.TryGetValue(propertyName, out var col) ? col.ColumnName : null;

    public IndexDefinition AddIndex(IReadOnlyList<string> propNames)
    {
        var existing = Indexes.FirstOrDefault(ix =>
            ix.IsFromConvention &&
            ix.PropertyNames.SequenceEqual(propNames, StringComparer.Ordinal));

        if (existing is not null)
        {
            if (!_isApplyingEntityConventions)
            {
                existing.IsFromConvention = false;
            }

            return existing;
        }

        var idx = new IndexDefinition(propNames)
        {
            IsFromConvention = _isApplyingEntityConventions
        };
        Indexes.Add(idx);
        return idx;
    }

    public void AddCheckConstraint(string name, string sql) =>
        CheckConstraints[name] = new CheckConstraintDefinition(name, rawSql: sql, sqlRenderer: null);

    public CheckConstraintDefinition AddCheckConstraint(
        string name,
        SqlExpressionRenderDelegate sqlRenderer)
    {
        var def = new CheckConstraintDefinition(name, rawSql: null, sqlRenderer: sqlRenderer);
        CheckConstraints[name] = def;
        return def;
    }

    public ExclusionConstraintDefinition AddExclusionConstraint(string name)
    {
        var def = new ExclusionConstraintDefinition(name);
        ExclusionConstraints[name] = def;
        return def;
    }

    public void SetTablespace(string? tablespace)
    {
        Tablespace = string.IsNullOrWhiteSpace(tablespace) ? null : tablespace.Trim();
    }

    public void SetStorageParameter(string name, string value)
    {
        StorageParameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        StorageParameters[name.Trim()] = value;
    }

    public void SetAnnotation(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (value is null)
        {
            Annotations.Remove(name);
        }
        else
        {
            Annotations[name] = value;
        }
    }

    public bool IsPostgresXmin(ColumnDefinition col)
    {
        var unwrapped = Nullable.GetUnderlyingType(col.ClrType) ?? col.ClrType;
        return Dialect == SqlDialect.PostgreSql && col.IsRowVersion && unwrapped == typeof(uint);
    }

    public string ResolveColumnSqlType(
        ColumnDefinition col,
        IReadOnlyDictionary<string, PostgresEnumSchema>? enumLookup = null) =>
        IsPostgresXmin(col)
            ? "xid"
            : SqlTypeResolver.Resolve(
                Dialect,
                col.ProviderClrType ?? col.ClrType,
                col.ExplicitSqlType,
                col.MaxLength,
                col.Precision,
                col.Scale,
                col.IsUnicode,
                enumLookup);

    public string ResolveIndexName(IndexDefinition ix, IReadOnlyList<string>? resolvedColumnNames = null)
    {
        if (!string.IsNullOrWhiteSpace(ix.IndexName))
        {
            return NamingFormatter.TruncateIdentifier(ix.IndexName, Dialect);
        }

        var cols = resolvedColumnNames ?? ix.PropertyNames
            .Select(p => ColumnsByProp.TryGetValue(p, out var c) ? c.ColumnName : p)
            .ToList();
        var prefix = NamingFormatter.FormatPrefix(ix.IsUnique ? "uq" : "ix", IdentifierCase);
        return NamingFormatter.TruncateIdentifier(
            $"{prefix}_{TableName}_{string.Join("_", cols)}",
            Dialect);
    }

    public void SetForeignKey(
        Type principalType,
        IReadOnlyList<string> depProps,
        IReadOnlyList<string> principalProps,
        string? name,
        DbReferentialAction onDelete,
        bool isCircular,
        bool isUnique = false)
    {
        ForeignKeys.RemoveAll(x => x.DependentProps.SequenceEqual(depProps, StringComparer.Ordinal));
        ForeignKeys.Add(new ForeignKeyDefinition(principalType, depProps, principalProps, name, onDelete, isCircular, isUnique));
    }
}

internal sealed class CheckConstraintDefinition
{
    private readonly string? _rawSql;
    private readonly SqlExpressionRenderDelegate? _sqlRenderer;

    public string Name { get; }
    public bool? DelimitIdentifiers { get; set; }
    public string? NestedColumnSeparator { get; set; }

    public CheckConstraintDefinition(
        string name,
        string? rawSql,
        SqlExpressionRenderDelegate? sqlRenderer)
    {
        Name = name;
        _rawSql = rawSql;
        _sqlRenderer = sqlRenderer;
    }

    public string RenderSql(
        SqlDialect dialect,
        SqlIdentifierCase identifierCase,
        SqlExpressionBuilderOptions defaults,
        Func<string, string?> resolveColumnName)
    {
        if (_rawSql is not null)
        {
            return _rawSql;
        }

        var delimit = DelimitIdentifiers ?? defaults.DelimitIdentifiers;
        var separator = NestedColumnSeparator ?? defaults.NestedColumnSeparator;
        return _sqlRenderer!(dialect, identifierCase, delimit, separator, resolveColumnName);
    }
}

internal sealed record ExclusionConstraintElementDefinition(
    string? PropertyName,
    string? ExpressionSql,
    string? OperatorClass,
    string Operator,
    Func<Func<string, string>, string>? ExpressionRenderer = null,
    SqlExpressionRenderDelegate? SqlExpressionRenderer = null);

internal sealed class ExclusionConstraintDefinition
{
    public string Name { get; }
    public string Method { get; set; } = "gist";
    public List<ExclusionConstraintElementDefinition> Elements { get; } = [];
    public List<string>? IncludedPropertyNames { get; set; }
    public Dictionary<string, string>? StorageParameters { get; set; }
    public string? Tablespace { get; set; }
    public string? FilterSql { get; set; }
    public SqlExpressionRenderDelegate? FilterRenderer { get; set; }

    public ExclusionConstraintDefinition(string name)
    {
        Name = name;
    }
}

internal sealed class ColumnDefinition
{
    public string PropertyName { get; }
    public Type ClrType { get; }
    public string ColumnName { get; set; }
    public string? PreviousColumnName { get; set; }
    public string? ExplicitSqlType { get; set; }
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool? IsUnicode { get; set; }
    public string? DefaultValueSql { get; set; }
    public bool IsIdentity { get; set; }
    public ValueGenerationMode ValueGenerated { get; set; }
    public string? ComputedColumnSql { get; set; }
    public SqlExpressionRenderDelegate? ComputedColumnSqlRenderer { get; set; }
    public bool IsComputedStored { get; set; }
    public string? Comment { get; set; }
    public bool IsConcurrencyToken { get; set; }
    public bool IsRowVersion { get; set; }
    public bool IsSystemColumn { get; set; }
    public Type? ProviderClrType { get; set; }
    public object? ValueConverter { get; set; }
    public Dictionary<string, object?> Annotations { get; } = new(StringComparer.Ordinal);

    public void SetAnnotation(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (value is null)
        {
            Annotations.Remove(name);
        }
        else
        {
            Annotations[name] = value;
        }
    }

    public ColumnDefinition(string propertyName, Type clrType, string columnName)
    {
        PropertyName = propertyName;
        ClrType = clrType;
        ColumnName = columnName;
    }
}

internal sealed class IndexDefinition
{
    public IReadOnlyList<string> PropertyNames { get; }
    public string? IndexName { get; set; }
    public bool IsUnique { get; set; }
    public string? FilterSql { get; set; }
    public SqlExpressionRenderDelegate? FilterRenderer { get; set; }
    public List<string>? IncludedPropertyNames { get; set; }
    public List<bool>? IsDescending { get; set; }
    public string? Method { get; set; }
    public string? Tablespace { get; set; }
    public Dictionary<string, string>? StorageParameters { get; set; }
    public Dictionary<string, string>? OperatorsByProperty { get; set; }
    public bool IsFromConvention { get; set; }

    public IndexDefinition(IReadOnlyList<string> propertyNames)
    {
        PropertyNames = propertyNames;
    }
}

internal sealed record ForeignKeyDefinition(
    Type PrincipalType,
    IReadOnlyList<string> DependentProps,
    IReadOnlyList<string> PrincipalProps,
    string? ConstraintName,
    DbReferentialAction OnDelete,
    bool IsCircular,
    bool IsUnique = false);

internal sealed class SequenceDefinition
{
    public Type ClrType { get; }
    public string Name { get; }
    public string? Schema { get; set; }
    public bool SchemaExplicitlySet { get; }
    public long StartValue { get; set; } = 1;
    public int IncrementBy { get; set; } = 1;
    public long? MinValue { get; set; }
    public long? MaxValue { get; set; }
    public bool IsCyclic { get; set; }

    public SequenceDefinition(Type clrType, string name, string? schema, bool schemaExplicitlySet)
    {
        ClrType = clrType;
        Name = name;
        Schema = schema;
        SchemaExplicitlySet = schemaExplicitlySet;
    }
}
