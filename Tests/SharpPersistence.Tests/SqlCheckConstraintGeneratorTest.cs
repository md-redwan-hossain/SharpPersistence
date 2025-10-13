using SharpPersistence.Enums;
using SharpPersistence.Generators;
using SharpPersistence.Tests.TestDependencyFiles;
using Shouldly;

namespace SharpPersistence.Tests;

public class SqlCheckConstraintGeneratorTest
{
    [Fact]
    public void AndCheckWithFourParams()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);

        var sql =
            "((is_cash = TRUE AND is_bank = FALSE AND is_mobile_bank = FALSE) OR (is_cash = FALSE AND is_bank = TRUE AND is_mobile_bank = FALSE) OR (is_cash = FALSE AND is_bank = FALSE AND is_mobile_bank = TRUE) OR (is_cash = FALSE AND is_bank = FALSE AND is_mobile_bank = FALSE))";

        var testSql = cc.Or(
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), true),
                cc.EqualTo(nameof(AccountHead.IsBank), false),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), false)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), false),
                cc.EqualTo(nameof(AccountHead.IsBank), true),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), false)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), false),
                cc.EqualTo(nameof(AccountHead.IsBank), false),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), true)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), false),
                cc.EqualTo(nameof(AccountHead.IsBank), false),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), false)
            )
        );

        testSql.ShouldBe(sql);
    }

    
    [Fact]
    public void AndCheckWithFourParamsWithIsOperator()
    {
        var cc = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitStringGlobalLevel: false);

        const string sql =
            "((is_cash IS TRUE AND is_bank IS FALSE AND is_mobile_bank IS FALSE) OR (is_cash IS FALSE AND is_bank IS TRUE AND is_mobile_bank IS FALSE) OR (is_cash IS FALSE AND is_bank IS FALSE AND is_mobile_bank IS TRUE) OR (is_cash IS FALSE AND is_bank IS FALSE AND is_mobile_bank IS FALSE))";

        var testSql = cc.Or(
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), true, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsBank), false, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), false, useIsOperator: true)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), false, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsBank), true, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), false, useIsOperator: true)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), false, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsBank), false, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), true, useIsOperator: true)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), false, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsBank), false, useIsOperator: true),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), false, useIsOperator: true)
            )
        );

        testSql.ShouldBe(sql);
    }
    
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

    [Theory]
    [InlineData(Rdbms.SqlServer, SqlNamingConvention.PascalCase)]
    [InlineData(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase)]
    [InlineData(Rdbms.MySql, SqlNamingConvention.UpperSnakeCase)]
    public void And_Or_Should_Generate_Correct_Sql(Rdbms rdbms, SqlNamingConvention naming)
    {
        var gen = new SqlCheckConstrainGenerator(rdbms, naming);
        gen.And("A", "B").ShouldContain("AND");
        gen.Or("A", "B").ShouldContain("OR");
        gen.And("A", "B", "C", "D").ShouldContain("AND");
        gen.Or("A", "B", "C", "D").ShouldContain("OR");
    }

    [Theory]
    [InlineData(Rdbms.SqlServer)]
    [InlineData(Rdbms.PostgreSql)]
    [InlineData(Rdbms.MySql)]
    public void In_NotIn_Should_Handle_Collections(Rdbms rdbms)
    {
        var gen = new SqlCheckConstrainGenerator(rdbms, SqlNamingConvention.PascalCase);
        gen.In("Col", new List<int> { 1, 2, 3 }).ShouldContain("IN");
        gen.In("Col", new List<string> { "a", "b" }).ShouldContain("IN");
        gen.In("Col", new List<Enum> { DayOfWeek.Monday, DayOfWeek.Tuesday }).ShouldContain("IN");
        gen.NotIn("Col", new List<int> { 1 }).ShouldContain("NOT IN");
        gen.NotIn("Col", new List<string>()).ShouldContain("NOT IN");
        gen.NotIn("Col", new List<Enum>()).ShouldContain("NOT IN");
    }

    [Fact]
    public void EqualTo_NotEqualTo_Should_Handle_All_Types()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        gen.EqualTo("Col", true).ShouldContain("= TRUE");
        gen.NotEqualTo("Col", false).ShouldContain("<> FALSE");
        gen.EqualTo("Col", 5, SqlDataType.Int).ShouldContain("= 5");
        gen.NotEqualTo("Col", 7, SqlDataType.Int).ShouldContain("<> 7");
        gen.EqualTo("Col", "Val", SqlOperandType.Value).ShouldContain("= 'Val'");
        gen.NotEqualTo("Col", "Val", SqlOperandType.Value).ShouldContain("<> 'Val'");
        gen.EqualTo("Col", DayOfWeek.Friday).ShouldContain(((int)DayOfWeek.Friday).ToString());
        gen.NotEqualTo("Col", DayOfWeek.Sunday).ShouldContain(((int)DayOfWeek.Sunday).ToString());
    }

    [Fact]
    public void Comparison_Operators_Should_Work_For_All_Overloads()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.UpperSnakeCase);
        gen.GreaterThan("Col", "Other", SqlOperandType.Column).ShouldContain("> ");
        gen.GreaterThan("Col", 10, SqlDataType.Int).ShouldContain("> 10");
        gen.GreaterThan("Col", DayOfWeek.Monday).ShouldContain(((int)DayOfWeek.Monday).ToString());
        gen.GreaterThanOrEqual("Col", "Other", SqlOperandType.Column).ShouldContain(">=");
        gen.GreaterThanOrEqual("Col", 1, SqlDataType.Int).ShouldContain(">= 1");
        gen.GreaterThanOrEqual("Col", DayOfWeek.Tuesday).ShouldContain(((int)DayOfWeek.Tuesday).ToString());
        gen.LessThan("Col", "Other", SqlOperandType.Column).ShouldContain("< ");
        gen.LessThan("Col", 2, SqlDataType.Int).ShouldContain("< 2");
        gen.LessThan("Col", DayOfWeek.Wednesday).ShouldContain(((int)DayOfWeek.Wednesday).ToString());
        gen.LessThanOrEqual("Col", "Other", SqlOperandType.Column).ShouldContain("<=");
        gen.LessThanOrEqual("Col", 3, SqlDataType.Int).ShouldContain("<= 3");
        gen.LessThanOrEqual("Col", DayOfWeek.Thursday).ShouldContain(((int)DayOfWeek.Thursday).ToString());
    }

    [Fact]
    public void Between_NotBetween_Should_Handle_All_Types()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase);
        gen.Between("Col", "a", "b").ShouldContain("BETWEEN");
        gen.Between("Col", 1, 2).ShouldContain("BETWEEN");
        gen.Between("Col", 1.1, 2.2).ShouldContain("BETWEEN");
        gen.NotBetween("Col", "a", "b").ShouldContain("NOT BETWEEN");
        gen.NotBetween("Col", 1, 2).ShouldContain("NOT BETWEEN");
        gen.NotBetween("Col", 1.1, 2.2).ShouldContain("NOT BETWEEN");
    }

    [Fact]
    public void IsNull_IsNotNull_Should_Work()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        gen.IsNull("Col").ShouldContain("IS NULL");
        gen.IsNotNull("Col").ShouldContain("IS NOT NULL");
    }

    [Fact]
    public void Handles_Empty_And_Null_Inputs_Gracefully()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        gen.And("", "").ShouldContain("AND");
        gen.Or("", "").ShouldContain("OR");
        gen.In("Col", new List<int>()).ShouldContain("IN");
        gen.NotIn("Col", new List<string>()).ShouldContain("NOT IN");
        gen.Between("Col", 0, 0).ShouldContain("BETWEEN");
        gen.NotBetween("Col", 0, 0).ShouldContain("NOT BETWEEN");
    }

    [Fact]
    public void DelimitString_Should_Use_Correct_Symbols()
    {
        var genPg = new SqlCheckConstrainGenerator(Rdbms.PostgreSql, SqlNamingConvention.PascalCase);
        var genMy = new SqlCheckConstrainGenerator(Rdbms.MySql, SqlNamingConvention.PascalCase);
        var genSql = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        genPg.IsNull("Col").ShouldContain("\"Col\"");
        genMy.IsNull("Col").ShouldContain("`Col`");
        genSql.IsNull("Col").ShouldContain("[Col]");
    }

    [Fact]
    public void TransformCase_Should_Respect_Naming_Convention()
    {
        var genLower = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.LowerSnakeCase);
        var genUpper = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.UpperSnakeCase);
        var genPascal = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        genUpper.In("TestColumn", new List<int> { 1 }).ShouldContain("TEST_COLUMN");
        genPascal.In("test_column", new List<int> { 1 }).ShouldContain("TestColumn");
        genLower.In("TestColumn", new List<int> { 1 }).ShouldContain("test_column");
    }

    [Fact]
    public void SqlString_Should_Escape_Single_Quotes()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        var sql = gen.EqualTo("Col", "O'Reilly", SqlOperandType.Value);
        sql.ShouldContain("'O''Reilly'");
    }

    [Fact]
    public void LengthOperatorHandler_Should_Apply_For_Text_Types()
    {
        var gen = new SqlCheckConstrainGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        var sql = gen.EqualTo("Col", 5, SqlDataType.VarChar);
        sql.ShouldContain("LEN(");
        sql = gen.NotEqualTo("Col", 5, SqlDataType.Text);
        sql.ShouldContain("LEN(");
    }
}