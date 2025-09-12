using SharpPersistence.Abstractions;
using SharpPersistence.Parsers;
using Shouldly;

namespace SharpPersistence.ReflectionTests;

public class SqlParserTest
{
    private readonly IParsedSqlStorage _parsedSqlStorage = new SqlParser().ParseFromCallingAssembly();

    [Fact]
    public void ParseFromStorage_Should_Not_Be_Empty()
    {
        _parsedSqlStorage.ShouldNotBeEmpty();
    }
}