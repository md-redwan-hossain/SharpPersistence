using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

internal sealed class SequenceSchema
{
    public string Name { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public string ClrTypeName { get; set; } = "long";
    public string SqlType { get; set; } = "bigint";
    public long StartValue { get; set; } = 1;
    public int IncrementBy { get; set; } = 1;
    public long? MinValue { get; set; }
    public long? MaxValue { get; set; }
    public bool IsCyclic { get; set; }

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
