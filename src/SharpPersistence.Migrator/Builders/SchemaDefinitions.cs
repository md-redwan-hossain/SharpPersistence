using System.Linq.Expressions;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

internal sealed class TableDefinition
{
    public Type EntityType { get; }
    public string TableName { get; set; }
    public string? Schema { get; set; }
    public string? PreviousTableName { get; set; }
    public string? PreviousSchema { get; set; }
    public string? Comment { get; set; }
    public bool IsUnlogged { get; set; }
    public string? Tablespace { get; set; }
    public Dictionary<string, string>? StorageParameters { get; set; }
    public LambdaExpression? QueryFilter { get; set; }
    public Dictionary<string, LambdaExpression?> NamedQueryFilters { get; } = new(StringComparer.Ordinal);
    public HashSet<string> IgnoredProperties { get; } = new(StringComparer.Ordinal);
    public SqlDialect Dialect { get; }
    public SqlIdentifierCase IdentifierCase { get; }
    public IReadOnlyList<string> PkProperties { get; private set; } = [];
    public string? PkName { get; private set; }
    public Dictionary<string, ColumnDefinition> ColumnsByProp { get; } = new(StringComparer.Ordinal);
    public List<IndexDefinition> Indexes { get; } = [];
    public Dictionary<string, string> CheckConstraints { get; } = new(StringComparer.Ordinal);
    public List<ForeignKeyDefinition> ForeignKeys { get; } = [];

    public TableDefinition(
        Type entityType,
        string tableName,
        string? schema,
        SqlDialect dialect,
        SqlIdentifierCase identifierCase)
    {
        EntityType = entityType;
        TableName = tableName;
        Schema = schema;
        Dialect = dialect;
        IdentifierCase = identifierCase;
    }

    public void SetPrimaryKey(IReadOnlyList<string> propNames, string? constraintName)
    {
        PkProperties = propNames;
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

    public ColumnBuilder GetOrAddColumn(string propName, Type clrType, bool returnFluent)
    {
        var col = GetOrAddColumn(propName, clrType);
        return new ColumnBuilder(col);
    }

    public void IgnoreProperty(string propName)
    {
        IgnoredProperties.Add(propName);
        ColumnsByProp.Remove(propName);
    }

    public IndexDefinition AddIndex(IReadOnlyList<string> propNames, string? indexName)
    {
        var idx = new IndexDefinition(propNames, indexName);
        Indexes.Add(idx);
        return idx;
    }

    public void AddCheckConstraint(string name, string sql) => CheckConstraints[name] = sql;

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
    public bool IsComputedStored { get; set; }
    public string? Comment { get; set; }
    public bool IsConcurrencyToken { get; set; }
    public bool IsRowVersion { get; set; }
    public bool IsSystemColumn { get; set; }
    public Type? ProviderClrType { get; set; }
    public object? ValueConverter { get; set; }

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
    public List<string>? IncludedPropertyNames { get; set; }
    public List<bool>? IsDescending { get; set; }
    public string? Method { get; set; }
    public string? Tablespace { get; set; }
    public Dictionary<string, string>? StorageParameters { get; set; }
    public Dictionary<string, string>? OperatorsByProperty { get; set; }

    public IndexDefinition(IReadOnlyList<string> propertyNames, string? indexName)
    {
        PropertyNames = propertyNames;
        IndexName = indexName;
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
