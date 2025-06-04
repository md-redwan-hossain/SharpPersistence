namespace SharpPersistence.Abstractions.Primitives;

public readonly struct SqlFile
{
    public string FileName { get; init; }

    public string Content { get; init; }

    public SqlFile()
    {
        FileName = string.Empty;
        Content = string.Empty;
    }
}