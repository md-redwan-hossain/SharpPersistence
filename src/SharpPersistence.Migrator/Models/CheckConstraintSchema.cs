namespace SharpPersistence.Migrator.Models;

public sealed class CheckConstraintSchema
{
    public string Name { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
}
