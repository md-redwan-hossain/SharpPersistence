namespace SharpPersistence.Abstractions.Primitives;

public readonly ref struct SqlLine
{
    public int Number { get; init; }

    public string Text { get; init; }

    public SqlLine()
    {
        Number = 0;
        Text = string.Empty;
    }
}