using SharpPersistence.Extensions;
using Shouldly;

namespace SharpPersistence.Tests;

public class EnumerableExtensionsTests
{
    [Fact]
    public void CursorPaginate()
    {
        var data = Enumerable.Range(1, 100).OrderBy(x => x).EnumerableOffsetPaginate(1, 10);
        data.ShouldNotBeEmpty();
    }
}