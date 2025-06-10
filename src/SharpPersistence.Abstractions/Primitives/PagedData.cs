namespace SharpPersistence.Abstractions.Primitives;

public record PagedData<T>(ICollection<T> Payload, long TotalCount)
{
    public static PagedData<T> CreateEmpty()
    {
        return new PagedData<T>([], 0);
    }
}

