using System.Text.Json.Serialization;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Models;

internal sealed class ForeignKeySchema
{
    public string Name { get; set; } = string.Empty;
    public List<string> DependentColumns { get; set; } = [];

    [JsonIgnore]
    public List<string> DependentProperties { get; set; } = [];

    [JsonIgnore]
    public string PrincipalEntityName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrincipalSchema { get; set; }

    public string PrincipalTable { get; set; } = string.Empty;
    public List<string> PrincipalColumns { get; set; } = [];

    [JsonIgnore]
    public List<string> PrincipalProperties { get; set; } = [];

    public DbReferentialAction OnDelete { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCircular { get; set; }
}
