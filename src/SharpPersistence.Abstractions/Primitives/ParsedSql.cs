namespace SharpPersistence.Abstractions.Primitives;

public readonly struct ParsedSql
{
    public string Name { get; init; }

    public string SqlBody { get; init; }

    public ParsedSql()
    {
        Name = string.Empty;
        SqlBody = string.Empty;
    }

    public override string ToString() =>
        $"-- #start# {Name}{Environment.NewLine}{SqlBody}{Environment.NewLine}-- #end# {Name}{Environment.NewLine}";
}