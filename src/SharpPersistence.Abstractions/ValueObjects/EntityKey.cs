namespace SharpPersistence.Abstractions.ValueObjects;

public readonly record struct EntityKey<TKey>
{
    public required TKey Key { get; init; }
    
        public static EntityKey<TKey> Create(TKey key)
    {
        return new EntityKey<TKey>
        {
            Key = key
        };
    }
}