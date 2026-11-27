using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

internal sealed class IndexSchema
{
    public string Name { get; set; } = string.Empty;
    public List<string> ColumnNames { get; set; } = [];

    [JsonIgnore]
    public List<string> PropertyNames { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUnique { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FilterSql { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? IncludedColumnNames { get; set; }

    [JsonIgnore]
    public List<string>? IncludedPropertyNames { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<bool>? IsDescending { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tablespace { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? StorageParameters { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? OperatorsByColumn { get; set; }

    [JsonIgnore]
    public Dictionary<string, string>? OperatorsByProperty { get; set; }
}
