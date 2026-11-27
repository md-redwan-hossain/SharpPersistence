using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

internal sealed class SequenceSchema
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }

    [JsonIgnore]
    public string ClrTypeName { get; set; } = "long";
    public string SqlType { get; set; } = "bigint";
    public long StartValue { get; set; } = 1;
    public int IncrementBy { get; set; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MinValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCyclic { get; set; }

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
