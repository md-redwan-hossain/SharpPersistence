using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

internal sealed class PostgresEnumSchema
{
    public string Name { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public string ClrTypeName { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = [];

    [JsonIgnore]
    public string QualifiedName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
