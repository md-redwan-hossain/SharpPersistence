using System.Text.Json.Serialization;

namespace SharpPersistence.Migrator.Models;

public sealed class DatabaseSchema
{
    [JsonConstructor]
    internal DatabaseSchema()
    {
    }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal List<PostgresExtensionSchema>? Extensions { get; set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal List<PostgresEnumSchema>? Enums { get; set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal List<SequenceSchema>? Sequences { get; set; }

    [JsonInclude]
    internal List<TableSchema> Tables { get; set; } = [];

    [JsonIgnore]
    internal Dictionary<Type, TableSchema> ByClrType { get; } = [];
}
