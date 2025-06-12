namespace SharpPersistence.Abstractions.ValueObjects;

public readonly record struct KeyData<TKey, TData>
{
    public required TKey Key { get; init; }
    public required TData Data { get; init; }

    public static KeyData<TKey, TData> Create(TKey key, TData data)
    {
        return new KeyData<TKey, TData>
        {
            Key = key,
            Data = data
        };
    }

    public void Deconstruct(out TKey key, out TData data)
    {
        key = Key;
        data = Data;
    }
}