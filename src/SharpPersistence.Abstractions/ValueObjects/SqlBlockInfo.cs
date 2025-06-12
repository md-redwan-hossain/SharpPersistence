namespace SharpPersistence.Abstractions.ValueObjects;

public readonly record struct SqlBlockInfo
{
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public bool StartFound { get; init; }
    public bool EndFound { get; init; }
}