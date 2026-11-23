using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

public sealed class DatabaseSchema
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostgresExtensionSchema>? Extensions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostgresEnumSchema>? Enums { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SequenceSchema>? Sequences { get; set; }

    public List<TableSchema> Tables { get; set; } = [];

    [JsonIgnore]
    public Dictionary<Type, TableSchema> ByClrType { get; } = [];
}
