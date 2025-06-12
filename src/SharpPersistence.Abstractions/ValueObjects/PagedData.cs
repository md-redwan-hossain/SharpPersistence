namespace SharpPersistence.Abstractions.ValueObjects;

public record PagedData<T>(ICollection<T> Payload, long TotalCount)
{
    public static PagedData<T> CreateEmpty()
    {
        return new PagedData<T>([], 0);
    }

    public void Deconstruct(out ICollection<T> payload, out long totalCount)
    {
        payload = Payload;
        totalCount = TotalCount;
    }
}