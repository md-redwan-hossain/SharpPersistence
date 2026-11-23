using System.Text.Json.Serialization;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Models;

public sealed class ForeignKeySchema
{
    public string Name { get; set; } = string.Empty;
    public List<string> DependentColumns { get; set; } = [];
    public List<string> DependentProperties { get; set; } = [];
    public string PrincipalEntityName { get; set; } = string.Empty;
    public string? PrincipalSchema { get; set; }
    public string PrincipalTable { get; set; } = string.Empty;
    public List<string> PrincipalColumns { get; set; } = [];
    public List<string> PrincipalProperties { get; set; } = [];
    public DbReferentialAction OnDelete { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCircular { get; set; }
}
