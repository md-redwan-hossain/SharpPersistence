using SharpPersistence.Abstractions;
using SharpPersistence.Parsers;
using Shouldly;

namespace SharpPersistence.Tests;

public class SqlParserTest
{
    private readonly IParsedSqlStorage _parsedSqlStorage = new SqlParser().ParseFromStorage();

    [Fact]
    public void ParseFromStorage_Should_Not_Be_Empty()
    {
        _parsedSqlStorage.ShouldNotBeEmpty();
    }

    [Fact]
    public void ParseFromStorage_Non_Existent_Unique_Tag_Should_Be_Null()
    {
        _parsedSqlStorage.TryGetParsedSql("demo", out var sql);
        sql.ShouldBeNull();
    }

    [Fact]
    public void ParseFromStorage_Existent_Unique_Tag_Should_Not_Be_Null()
    {
        _parsedSqlStorage.TryGetParsedSql("GetAllUsers", out var sql);
        sql.ShouldNotBeNull();
    }

    [Fact]
    public void ParseFromStorage_Existent_Unique_Tag_Should_Match()
    {
        const string sqlBody = """
                               SELECT *
                               FROM users
                               """;

        _parsedSqlStorage["GetAllUsers"].ShouldContainWithoutWhitespace(sqlBody);
    }

    [Fact]
    public void ParseFromStorage_Existent_Unique_Tag_Should_Contain_Given_String()
    {
        _parsedSqlStorage.TryGetParsedSql("GetActiveUsers", out var sql);

        sql.ShouldNotBeNull();
        sql.ShouldContainWithoutWhitespace("WHERE active = 1");
    }
}