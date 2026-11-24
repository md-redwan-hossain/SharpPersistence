using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

internal sealed class TableSchema
{
    public string EntityName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? Schema { get; set; }

    [JsonIgnore]
    public string? PreviousTableName { get; set; }

    [JsonIgnore]
    public string? PreviousSchema { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Comment { get; set; }

    [JsonIgnore]
    public bool ExcludeFromMigrations { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUnlogged { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tablespace { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? StorageParameters { get; set; }

    [JsonIgnore]
    public List<string> IgnoredProperties { get; set; } = [];

    [JsonIgnore]
    public LambdaExpression? QueryFilter { get; set; }

    [JsonIgnore]
    public Dictionary<string, LambdaExpression?> NamedQueryFilters { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public Dictionary<string, object?> Annotations { get; set; } = new(StringComparer.Ordinal);

    public PrimaryKeySchema? PrimaryKey { get; set; }
    public List<ColumnSchema> Columns { get; set; } = [];
    public List<IndexSchema> Indexes { get; set; } = [];
    public List<CheckConstraintSchema> CheckConstraints { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExclusionConstraintSchema>? ExclusionConstraints { get; set; }

    public List<ForeignKeySchema> ForeignKeys { get; set; } = [];

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? TableName : $"{Schema}.{TableName}";
}
