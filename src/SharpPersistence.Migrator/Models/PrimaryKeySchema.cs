namespace SharpPersistence.Migrator.Models;

internal sealed class PrimaryKeySchema
{
    public string Name { get; set; } = string.Empty;
    public List<string> ColumnNames { get; set; } = [];
    public List<string> PropertyNames { get; set; } = [];
}
