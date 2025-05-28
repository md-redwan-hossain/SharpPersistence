using SharpPersistence.Enums;
using Shouldly;

namespace SharpPersistence.Tests;

public class SqlCheckConstraintGeneratorTest
{
    [Fact]
    public void AndCheckWithParams()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        var sql =
            $"((is_verified = {bool.FalseString} AND phone IS NULL AND otp IS NULL) OR (is_verified = {bool.TrueString} AND phone IS NOT NULL AND otp IS NOT NULL))";

        var testSql = cc.Or(
            cc.And(cc.EqualTo("is_verified", false), cc.IsNull("phone"), cc.IsNull("otp")),
            cc.And(cc.EqualTo("is_verified", true), cc.IsNotNull("phone"), cc.IsNotNull("otp"))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void AndCheckWithoutParams()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        var sql =
            $"((is_verified = {bool.FalseString} AND phone IS NULL) OR (is_verified = {bool.TrueString} AND phone IS NOT NULL))";

        var testSql = cc.Or(
            cc.And(cc.EqualTo("is_verified", false), cc.IsNull("phone")),
            cc.And(cc.EqualTo("is_verified", true), cc.IsNotNull("phone"))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void TrueStringCheck_String()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        var sql = $"address = {bool.TrueString}";
        var testSql = cc.EqualTo("address", true);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void FalseStringCheck_String()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        var sql = $"address = {bool.FalseString}";
        var testSql = cc.EqualTo("address", false);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void NotTrueCheck_String()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        var sql = $"address <> {bool.TrueString}";
        var testSql = cc.NotEqualTo("address", true);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void IsNullCheck_String()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        const string sql = "address IS NULL";
        var testSql = cc.IsNull("address");
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void IsNotNullCheck_String()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        const string sql = "address IS NOT NULL";
        var testSql = cc.IsNotNull("address");
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void InCheck_String()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        const string sql = "job_title IN ('Design Engineer', 'Tool Designer')";
        var testSql = cc.In("job_title", ["Design Engineer", "Tool Designer"]);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void BetweenCheck_String_GlobalDelimitFalse()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        const string sql = "buy_price BETWEEN 90 AND 100";
        var testSql = cc.Between("buy_price", 90, 100);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void BetweenCheck_String_MethodDelimitTrue()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);
        const string sql = "`buy_price` BETWEEN 90 AND 100";
        var testSql = cc.Between("buy_price", 90, 100, delimitColumnName: true);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void GreaterThanCheck_String_As_Value()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase);
        const string sql = "CHAR_LENGTH(`sell_price`) > 100";
        var testSql = cc.GreaterThan("sell_price", 100, SqlDataType.VarChar);
        testSql.ShouldBe(sql);
    }


    [Fact]
    public void GreaterThanCheck_Int_As_Value()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase);
        const string sql = "`sell_price` > 100";
        var testSql = cc.GreaterThan("sell_price", 100, SqlDataType.Int);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void GreaterThanCheck_String_As_Column_DelimitLeftOperand()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase);
        const string sql = "sell_price > `buy_price`";
        var testSql = cc.GreaterThan("sell_price", "buy_price", SqlOperandType.Column, delimitLeftOperand: false);
        testSql.ShouldBe(sql);
    }
}