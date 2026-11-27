using System.Text.Json.Serialization;
using SharpPersistence.Enums;

namespace SharpPersistence.Migrator.Models;

public sealed class DatabaseModel
{
    internal const int CurrentSnapshotVersion = 1;

    [JsonConstructor]
    internal DatabaseModel()
    {
    }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal int SnapshotVersion { get; set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal SqlDialect? Dialect { get; set; }

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

    [JsonIgnore]
    internal Dictionary<string, Type> ClrTypeByEntityName { get; } = new(StringComparer.Ordinal);
}
