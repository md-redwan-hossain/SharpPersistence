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

    [Fact]
    public void ParseFromStorage_Should_Be_Case_Insensitive_For_Lookups()
    {
        // All variations should retrieve the same SQL
        _parsedSqlStorage.TryGetParsedSql("GetAllUsers", out var sql1);
        _parsedSqlStorage.TryGetParsedSql("getAllusers", out var sql2);
        _parsedSqlStorage.TryGetParsedSql("GETALLUSERS", out var sql3);

        sql1.ShouldNotBeNull();
        sql2.ShouldNotBeNull();
        sql3.ShouldNotBeNull();

        sql1.ShouldBe(sql2);
        sql2.ShouldBe(sql3);
    }

    [Fact]
    public void ParseFromStorage_Should_Detect_Case_Insensitive_Duplicates()
    {
        const string sqlWithDuplicates = """
                                         -- #start# mytesttag
                                         SELECT 2
                                         -- #end# mytesttag

                                         -- #start# MyTestTag
                                         SELECT 1
                                         -- #end# MyTestTag
                                         """;

        var exception = Should.Throw<InvalidOperationException>(() =>
        {
            new SqlParser().ParseFromString(sqlWithDuplicates);
        });

        exception.Message.ShouldContain("Duplicate tag 'mytesttag' found");
    }

    [Fact]
    public void ParseFromStorage_Should_Preserve_Original_Casing_In_Error_Messages()
    {
        const string sqlWithError = """
                                    -- #start# MySpecialTag
                                    SELECT 1
                                    -- Missing end tag with original casing
                                    """;

        var exception = Should.Throw<InvalidOperationException>(() =>
        {
            new SqlParser().ParseFromString(sqlWithError);
        });

        // Error message should show "MySpecialTag"
        exception.Message.ShouldContain("MySpecialTag");
    }

    [Fact]
    public void ParseFromStorage_Case_Insensitive_Duplicate_Should_Show_Second_Tag_Casing()
    {
        const string sqlWithDuplicates = """
                                         -- #start# FirstCasing
                                         SELECT 1
                                         -- #end# FirstCasing

                                         -- #start# FIRSTCASING
                                         SELECT 2
                                         -- #end# FIRSTCASING
                                         """;

        var exception = Should.Throw<InvalidOperationException>(() =>
        {
            new SqlParser().ParseFromString(sqlWithDuplicates);
        });

        // Should show the casing of the duplicate (second occurrence)
        exception.Message.ShouldContain("FIRSTCASING");
        exception.Message.ShouldContain("Duplicate tag");
    }
}