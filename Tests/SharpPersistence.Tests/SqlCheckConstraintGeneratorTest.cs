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
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);

        const string sql =
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
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);

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
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        var sql =
            $"((is_verified = {bool.FalseString.ToUpperInvariant()} AND phone IS NULL AND otp IS NULL) OR (is_verified = {bool.TrueString.ToUpperInvariant()} AND phone IS NOT NULL AND otp IS NOT NULL))";

        var testSql = cc.Or(
            cc.And(cc.EqualTo("is_verified", false), cc.IsNull("phone"), cc.IsNull("otp")),
            cc.And(cc.EqualTo("is_verified", true), cc.IsNotNull("phone"), cc.IsNotNull("otp"))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void AndCheckWithoutParams()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        var sql =
            $"((is_verified = {bool.FalseString.ToUpperInvariant()} AND phone IS NULL) OR (is_verified = {bool.TrueString.ToUpperInvariant()} AND phone IS NOT NULL))";

        var testSql = cc.Or(
            cc.And(cc.EqualTo("is_verified", false), cc.IsNull("phone")),
            cc.And(cc.EqualTo("is_verified", true), cc.IsNotNull("phone"))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void TrueStringCheck_String()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        var sql = $"address = {bool.TrueString.ToUpperInvariant()}";
        var testSql = cc.EqualTo("address", true);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Math_Equal()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, 
            SqlNamingConvention.LowerSnakeCase, delimitString: false);

        const string sql = "balance + remaining - tax = 50";
        var testSql = cc.Math(
            [
                ("balance", SqlMathOperator.Add),
                ("remaining", SqlMathOperator.Subtract),
                ("tax", null)
            ],
            SqlComparisonOperator.Equal, 50);

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void FalseStringCheck_String()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        var sql = $"address = {bool.FalseString.ToUpperInvariant()}";
        var testSql = cc.EqualTo("address", false);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void NotTrueCheck_String()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        var sql = $"address <> {bool.TrueString.ToUpperInvariant()}";
        var testSql = cc.NotEqualTo("address", true);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void IsNullCheck_String()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        const string sql = "address IS NULL";
        var testSql = cc.IsNull("address");
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void IsNotNullCheck_String()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        const string sql = "address IS NOT NULL";
        var testSql = cc.IsNotNull("address");
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void InCheck_String()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        const string sql = "job_title IN ('Design Engineer', 'Tool Designer')";
        var testSql = cc.In("job_title", ["Design Engineer", "Tool Designer"]);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void BetweenCheck_String_GlobalDelimitFalse()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        const string sql = "buy_price BETWEEN 90 AND 100";
        var testSql = cc.Between("buy_price", 90, 100);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void BetweenCheck_String_MethodDelimitTrue()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase,
            delimitString: false);
        const string sql = "`buy_price` BETWEEN 90 AND 100";
        var testSql = cc.Between("buy_price", 90, 100, delimitColumnName: true);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void GreaterThanCheck_String_As_Value()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase);
        const string sql = "CHAR_LENGTH(`sell_price`) > 100";
        var testSql = cc.GreaterThan("sell_price", 100, SqlDataType.VarChar);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void GreaterThanCheck_Int_As_Value()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase);
        const string sql = "`sell_price` > 100";
        var testSql = cc.GreaterThan("sell_price", 100, SqlDataType.Int);
        testSql.ShouldBe(sql);
    }

    [Fact]
    public void GreaterThanCheck_String_As_Column_DelimitLeftOperand()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.LowerSnakeCase);
        const string sql = "sell_price > `buy_price`";
        var testSql = cc.GreaterThan("sell_price", "buy_price", SqlOperandType.Column, delimitLeftOperand: false);
        testSql.ShouldBe(sql);
    }

    [Theory]
    [InlineData(Rdbms.SqlServer, SqlNamingConvention.PascalCase)]
    [InlineData(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase)]
    [InlineData(Rdbms.MySql, SqlNamingConvention.UpperSnakeCase)]
    [InlineData(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase)]
    public void And_Or_Should_Generate_Correct_Sql(Rdbms rdbms, SqlNamingConvention naming)
    {
        var gen = new SqlCheckConstraintGenerator(rdbms, naming);
        gen.And("A", "B").ShouldContain("AND");
        gen.Or("A", "B").ShouldContain("OR");
        gen.And("A", "B", "C", "D").ShouldContain("AND");
        gen.Or("A", "B", "C", "D").ShouldContain("OR");
    }

    [Theory]
    [InlineData(Rdbms.SqlServer)]
    [InlineData(Rdbms.PostgreSql)]
    [InlineData(Rdbms.MySql)]
    [InlineData(Rdbms.Oracle)]
    public void In_NotIn_Should_Handle_Collections(Rdbms rdbms)
    {
        var gen = new SqlCheckConstraintGenerator(rdbms, SqlNamingConvention.PascalCase);
        gen.In("Col", [1, 2, 3]).ShouldContain("IN");
        gen.In("Col", ["a", "b"]).ShouldContain("IN");
        gen.In("Col", [DayOfWeek.Monday, DayOfWeek.Tuesday]).ShouldContain("IN");
        gen.NotIn("Col", [1]).ShouldContain("NOT IN");
    }

    [Fact]
    public void EqualTo_NotEqualTo_Should_Handle_All_Types()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
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
        var gen = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.UpperSnakeCase);
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
        var gen = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase);
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
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        gen.IsNull("Col").ShouldContain("IS NULL");
        gen.IsNotNull("Col").ShouldContain("IS NOT NULL");
    }

    [Fact]
    public void Handles_Empty_And_Null_Inputs_Gracefully()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        gen.And("", "").ShouldContain("AND");
        gen.Or("", "").ShouldContain("OR");
        gen.Between("Col", 0, 0).ShouldContain("BETWEEN");
        gen.NotBetween("Col", 0, 0).ShouldContain("NOT BETWEEN");
    }

    [Fact]
    public void In_And_NotIn_Should_Throw_On_Empty_Collections()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);

        Should.Throw<ArgumentException>(() => gen.In("Col", new List<int>()));
        Should.Throw<ArgumentException>(() => gen.In("Col", new List<string>()));
        Should.Throw<ArgumentException>(() => gen.In("Col", new List<Enum>()));
        Should.Throw<ArgumentException>(() => gen.NotIn("Col", new List<int>()));
        Should.Throw<ArgumentException>(() => gen.NotIn("Col", new List<string>()));
        Should.Throw<ArgumentException>(() => gen.NotIn("Col", new List<Enum>()));
    }

    [Fact]
    public void DelimitString_Should_Use_Correct_Symbols()
    {
        var genPg = new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.PascalCase);
        var genMy = new SqlCheckConstraintGenerator(Rdbms.MySql, SqlNamingConvention.PascalCase);
        var genSql = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        var genOra = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.PascalCase);
        genPg.IsNull("Col").ShouldContain("""
                                          "Col"
                                          """);
        genMy.IsNull("Col").ShouldContain("`Col`");
        genSql.IsNull("Col").ShouldContain("[Col]");
        genOra.IsNull("Col").ShouldContain("""
                                           "Col"
                                           """);
    }

    [Fact]
    public void TransformCase_Should_Respect_Naming_Convention()
    {
        var genLower = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.LowerSnakeCase);
        var genUpper = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.UpperSnakeCase);
        var genPascal = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        genUpper.In("TestColumn", new List<int> { 1 }).ShouldContain("TEST_COLUMN");
        genPascal.In("test_column", new List<int> { 1 }).ShouldContain("TestColumn");
        genLower.In("TestColumn", new List<int> { 1 }).ShouldContain("test_column");
    }

    [Fact]
    public void SqlString_Should_Escape_Single_Quotes()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        var sql = gen.EqualTo("Col", "O'Reilly", SqlOperandType.Value);
        sql.ShouldContain("'O''Reilly'");
    }

    [Fact]
    public void LengthOperatorHandler_Should_Apply_For_Text_Types()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        var sql = gen.EqualTo("Col", 5, SqlDataType.VarChar);
        sql.ShouldContain("LEN(");
        sql = gen.NotEqualTo("Col", 5, SqlDataType.Text);
        sql.ShouldContain("LEN(");
    }

    [Fact]
    public void LengthOperatorHandler_Oracle_Should_Use_Length()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.PascalCase);
        gen.EqualTo("Name", 5, SqlDataType.VarChar).ShouldContain("LENGTH(");
        gen.NotEqualTo("Name", 5, SqlDataType.Text).ShouldContain("LENGTH(");
    }

    [Fact]
    public void Default_Ctor_Should_Render_True_False()
    {
        var gen = new SqlCheckConstraintGenerator(Rdbms.SqlServer, SqlNamingConvention.PascalCase);
        gen.EqualTo("Col", true).ShouldContain("= TRUE");
        gen.EqualTo("Col", false).ShouldContain("= FALSE");
    }

    [Fact]
    public void Oracle_UpperSnakeCase_Should_Delimit_With_Double_Quotes()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase);
        cc.EqualTo("IsActive", 1, SqlDataType.Int).ShouldBe("""
                                                            "IS_ACTIVE" = 1
                                                            """);
    }

    [Fact]
    public void Oracle_GreaterThan_VarChar_Should_Use_Length()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase);
        cc.GreaterThan("SellPrice", 100, SqlDataType.VarChar).ShouldBe("""
                                                                       LENGTH("SELL_PRICE") > 100
                                                                       """);
    }

    [Fact]
    public void Oracle_Flag_Column_As_Int_Should_Render_Numeric_Literals()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase,
            delimitString: false);
        cc.EqualTo("IsActive", 1, SqlDataType.Int).ShouldBe("IS_ACTIVE = 1");
        cc.NotEqualTo("IsActive", 0, SqlDataType.Int).ShouldBe("IS_ACTIVE <> 0");
    }

    [Fact]
    public void Oracle_Flag_Column_As_Char_Should_Render_Quoted_Literals()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase,
            delimitString: false);
        cc.EqualTo("IsCash", "Y", SqlOperandType.Value).ShouldBe("IS_CASH = 'Y'");
        cc.EqualTo("IsCash", "N", SqlOperandType.Value).ShouldBe("IS_CASH = 'N'");
    }

    [Fact]
    public void Oracle_Between_With_Method_Delimit_Override()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase,
            delimitString: false);
        cc.Between("BuyPrice", 90, 100, delimitColumnName: true).ShouldBe("""
                                                                          "BUY_PRICE" BETWEEN 90 AND 100
                                                                          """);
    }

    [Fact]
    public void Oracle_AndOr_FourWay_AccountHead_Flags()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase,
            delimitString: false);

        const string sql =
            "((IS_CASH = 1 AND IS_BANK = 0 AND IS_MOBILE_BANK = 0) OR (IS_CASH = 0 AND IS_BANK = 1 AND IS_MOBILE_BANK = 0) OR (IS_CASH = 0 AND IS_BANK = 0 AND IS_MOBILE_BANK = 1) OR (IS_CASH = 0 AND IS_BANK = 0 AND IS_MOBILE_BANK = 0))";

        var testSql = cc.Or(
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 1, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 0, SqlDataType.Int)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 1, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 0, SqlDataType.Int)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 1, SqlDataType.Int)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 0, SqlDataType.Int)
            )
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_AndOr_Verified_Phone_Otp()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase,
            delimitString: false);

        const string sql =
            "((IS_VERIFIED = 0 AND PHONE IS NULL AND OTP IS NULL) OR (IS_VERIFIED = 1 AND PHONE IS NOT NULL AND OTP IS NOT NULL))";

        var testSql = cc.Or(
            cc.And(
                cc.EqualTo("IsVerified", 0, SqlDataType.Int),
                cc.IsNull("Phone"),
                cc.IsNull("Otp")
            ),
            cc.And(
                cc.EqualTo("IsVerified", 1, SqlDataType.Int),
                cc.IsNotNull("Phone"),
                cc.IsNotNull("Otp")
            )
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_AndOr_Debit_Credit_Entry()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase,
            delimitString: false);

        const string sql =
            "((CREDIT_AMOUNT >= 0 AND DEBIT_AMOUNT = 0) OR (DEBIT_AMOUNT >= 0 AND CREDIT_AMOUNT = 0))";

        var testSql = cc.Or(
            cc.And(
                cc.GreaterThanOrEqual("CreditAmount", 0, SqlDataType.Decimal),
                cc.EqualTo("DebitAmount", 0, SqlDataType.Decimal)
            ),
            cc.And(
                cc.GreaterThanOrEqual("DebitAmount", 0, SqlDataType.Decimal),
                cc.EqualTo("CreditAmount", 0, SqlDataType.Decimal)
            )
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_AndOr_FourWay_With_Delimiters()
    {
        var cc = new SqlCheckConstraintGenerator(Rdbms.Oracle, SqlNamingConvention.UpperSnakeCase);

        const string sql =
            """
            (("IS_CASH" = 1 AND "IS_BANK" = 0 AND "IS_MOBILE_BANK" = 0) OR ("IS_CASH" = 0 AND "IS_BANK" = 1 AND "IS_MOBILE_BANK" = 0) OR ("IS_CASH" = 0 AND "IS_BANK" = 0 AND "IS_MOBILE_BANK" = 1) OR ("IS_CASH" = 0 AND "IS_BANK" = 0 AND "IS_MOBILE_BANK" = 0))
            """;

        var testSql = cc.Or(
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 1, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 0, SqlDataType.Int)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 1, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 0, SqlDataType.Int)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 1, SqlDataType.Int)
            ),
            cc.And(
                cc.EqualTo(nameof(AccountHead.IsCash), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsBank), 0, SqlDataType.Int),
                cc.EqualTo(nameof(AccountHead.IsMobileBank), 0, SqlDataType.Int)
            )
        );

        testSql.ShouldBe(sql);
    }
}