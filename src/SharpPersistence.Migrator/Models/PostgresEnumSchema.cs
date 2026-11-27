using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

internal sealed class PostgresEnumSchema
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }

    [JsonIgnore]
    public string ClrTypeName { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
