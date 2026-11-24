using SharpPersistence.Enums;
using SharpPersistence.Tests.TestDependencyFiles;
using Shouldly;

namespace SharpPersistence.Tests;

public class SqlCheckConstraintBuilderParityTests
{
    private sealed class ParityEntity
    {
        public string Col { get; set; } = string.Empty;
        public string Other { get; set; } = string.Empty;
        public int TestColumn { get; set; }
        public int test_column { get; set; }
        public string Name { get; set; } = string.Empty;
        public int IsActive { get; set; }
        public decimal SellPrice { get; set; }
        public string IsCash { get; set; } = string.Empty;
        public int BuyPrice { get; set; }
        public int IsVerified { get; set; }
        public string? Phone { get; set; }
        public string? Otp { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal DebitAmount { get; set; }
    }

    [Fact]
    public void AndCheckWithFourParams()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };
        var ah = cc.For<AccountHead>();

        const string sql =
            "((is_cash = TRUE AND is_bank = FALSE AND is_mobile_bank = FALSE) OR (is_cash = FALSE AND is_bank = TRUE AND is_mobile_bank = FALSE) OR (is_cash = FALSE AND is_bank = FALSE AND is_mobile_bank = TRUE) OR (is_cash = FALSE AND is_bank = FALSE AND is_mobile_bank = FALSE))";

        var testSql = cc.Or(
            cc.And(
                ah.Column(x => x.IsCash).EqualTo(true),
                ah.Column(x => x.IsBank).EqualTo(false),
                ah.Column(x => x.IsMobileBank).EqualTo(false)
            ),
            cc.And(
                ah.Column(x => x.IsCash).EqualTo(false),
                ah.Column(x => x.IsBank).EqualTo(true),
                ah.Column(x => x.IsMobileBank).EqualTo(false)
            ),
            cc.And(
                ah.Column(x => x.IsCash).EqualTo(false),
                ah.Column(x => x.IsBank).EqualTo(false),
                ah.Column(x => x.IsMobileBank).EqualTo(true)
            ),
            cc.And(
                ah.Column(x => x.IsCash).EqualTo(false),
                ah.Column(x => x.IsBank).EqualTo(false),
                ah.Column(x => x.IsMobileBank).EqualTo(false)
            )
        );

        ((string)testSql).ShouldBe(sql);
    }

    [Fact]
    public void AndCheckWithParams()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        const string sql =
            "((is_verified = FALSE AND phone IS NULL AND otp IS NULL) OR (is_verified = TRUE AND phone IS NOT NULL AND otp IS NOT NULL))";

        string testSql = cc.Or(
            cc.And(cc.RawColumn<bool>("is_verified").EqualTo(false), cc.RawColumn<string?>("phone").IsNull(),
                cc.RawColumn<string?>("otp").IsNull()),
            cc.And(cc.RawColumn<bool>("is_verified").EqualTo(true), cc.RawColumn<string?>("phone").IsNotNull(),
                cc.RawColumn<string?>("otp").IsNotNull())
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void AndCheckWithoutParams()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        const string sql =
            "((is_verified = FALSE AND phone IS NULL) OR (is_verified = TRUE AND phone IS NOT NULL))";

        string testSql = cc.Or(
            cc.And(cc.RawColumn<bool>("is_verified").EqualTo(false), cc.RawColumn<string?>("phone").IsNull()),
            cc.And(cc.RawColumn<bool>("is_verified").EqualTo(true), cc.RawColumn<string?>("phone").IsNotNull())
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void TrueStringCheck_String()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<bool>("address").EqualTo(true).ShouldRender("address = TRUE");
    }

    [Fact]
    public void Math_Equal()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<decimal>("balance")
            .Math()
            .Plus(cc.RawColumn<decimal>("remaining").Math())
            .Minus(cc.RawColumn<decimal>("tax").Math())
            .EqualTo(50)
            .ShouldRender("balance + remaining - tax = 50");
    }

    [Fact]
    public void FalseStringCheck_String()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<bool>("address").EqualTo(false).ShouldRender("address = FALSE");
    }

    [Fact]
    public void NotTrueCheck_String()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<bool>("address").NotEqualTo(true).ShouldRender("address <> TRUE");
    }

    [Fact]
    public void IsNullCheck_String()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<string?>("address").IsNull().ShouldRender("address IS NULL");
    }

    [Fact]
    public void IsNotNullCheck_String()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<string?>("address").IsNotNull().ShouldRender("address IS NOT NULL");
    }

    [Fact]
    public void InCheck_String()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<string>("job_title")
            .In("Design Engineer", "Tool Designer")
            .ShouldRender("job_title IN ('Design Engineer', 'Tool Designer')");
    }

    [Fact]
    public void BetweenCheck_String_GlobalDelimitFalse()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.MySql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<int>("buy_price").Between(90, 100).ShouldRender("buy_price BETWEEN 90 AND 100");
    }

    [Fact]
    public void BetweenCheck_String_MethodDelimitTrue()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.MySql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.RawColumn<int>("buy_price").WithDelimiter().Between(90, 100).ShouldRender("`buy_price` BETWEEN 90 AND 100");
    }

    [Fact]
    public void GreaterThanCheck_String_As_Value()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.MySql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };

        cc.RawColumn<string>("sell_price").Length().GreaterThan(100).ShouldRender("CHAR_LENGTH(`sell_price`) > 100");
    }

    [Fact]
    public void GreaterThanCheck_Int_As_Value()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.MySql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };

        cc.RawColumn<int>("sell_price").GreaterThan(100).ShouldRender("`sell_price` > 100");
    }

    [Fact]
    public void GreaterThanCheck_String_As_Column_DelimitLeftOperand()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.MySql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };

        cc.RawColumn<decimal>("sell_price")
            .WithoutDelimiter()
            .GreaterThanRawColumn("buy_price")
            .ShouldRender("sell_price > `buy_price`");
    }

    [Theory]
    [InlineData(SqlDialect.SqlServer, SqlIdentifierCase.PascalCase)]
    [InlineData(SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase)]
    [InlineData(SqlDialect.MySql, SqlIdentifierCase.UpperSnakeCase)]
    [InlineData(SqlDialect.Oracle, SqlIdentifierCase.UpperSnakeCase)]
    [InlineData(SqlDialect.Sqlite, SqlIdentifierCase.LowerSnakeCase)]
    public void And_Or_Should_Generate_Correct_Sql(SqlDialect dialect, SqlIdentifierCase naming)
    {
        var cc = new SqlCheckConstraintBuilder
        { Dialect = dialect, IdentifierCase = naming, DelimitIdentifiers = true };
        var a = cc.RawPredicate("A");
        var b = cc.RawPredicate("B");
        var c = cc.RawPredicate("C");
        var d = cc.RawPredicate("D");

        ((string)cc.And(a, b)).ShouldContain("AND");
        ((string)cc.Or(a, b)).ShouldContain("OR");
        ((string)cc.And(a, b, c, d)).ShouldContain("AND");
        ((string)cc.Or(a, b, c, d)).ShouldContain("OR");
    }

    [Theory]
    [InlineData(SqlDialect.SqlServer)]
    [InlineData(SqlDialect.PostgreSql)]
    [InlineData(SqlDialect.MySql)]
    [InlineData(SqlDialect.Oracle)]
    [InlineData(SqlDialect.Sqlite)]
    public void In_NotIn_Should_Handle_Collections(SqlDialect dialect)
    {
        var cc = new SqlCheckConstraintBuilder
        { Dialect = dialect, IdentifierCase = SqlIdentifierCase.PascalCase, DelimitIdentifiers = true };
        ((string)cc.RawColumn<int>("Col").In(1, 2, 3)).ShouldContain("IN");
        ((string)cc.RawColumn<string>("Col").In("a", "b")).ShouldContain("IN");
        ((string)cc.RawColumn<DayOfWeek>("Col").In(DayOfWeek.Monday, DayOfWeek.Tuesday)).ShouldContain("IN");
        ((string)cc.RawColumn<int>("Col").NotIn(1)).ShouldContain("NOT IN");
    }

    [Fact]
    public void EqualTo_NotEqualTo_Should_Handle_All_Types()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };
        ((string)cc.RawColumn<bool>("Col").EqualTo(true)).ShouldContain("= 1");
        ((string)cc.RawColumn<bool>("Col").NotEqualTo(false)).ShouldContain("<> 0");
        ((string)cc.RawColumn<int>("Col").EqualTo(5)).ShouldContain("= 5");
        ((string)cc.RawColumn<int>("Col").NotEqualTo(7)).ShouldContain("<> 7");
        ((string)cc.RawColumn<string>("Col").EqualTo("Val")).ShouldContain("= 'Val'");
        ((string)cc.RawColumn<string>("Col").NotEqualTo("Val")).ShouldContain("<> 'Val'");
        ((string)cc.RawColumn<DayOfWeek>("Col").EqualTo(DayOfWeek.Friday)).ShouldContain(((int)DayOfWeek.Friday)
            .ToString());
        ((string)cc.RawColumn<DayOfWeek>("Col").NotEqualTo(DayOfWeek.Sunday)).ShouldContain(((int)DayOfWeek.Sunday)
            .ToString());
    }

    [Fact]
    public void Comparison_Operators_Should_Work_For_All_Types()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.MySql,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };
        var e = cc.For<ParityEntity>();

        ((string)e.Column(x => x.Col).GreaterThan(x => x.Other)).ShouldContain("> ");
        ((string)e.Column(x => x.Col).GreaterThan("10")).ShouldContain("> '10'");
        ((string)cc.RawColumn<int>("Col").GreaterThan(10)).ShouldContain("> 10");
        ((string)cc.RawColumn<DayOfWeek>("Col").GreaterThan(DayOfWeek.Monday)).ShouldContain(((int)DayOfWeek.Monday)
            .ToString());
        ((string)e.Column(x => x.Col).GreaterThanOrEqual(x => x.Other)).ShouldContain(">=");
        ((string)cc.RawColumn<int>("Col").GreaterThanOrEqual(1)).ShouldContain(">= 1");
        ((string)cc.RawColumn<DayOfWeek>("Col").GreaterThanOrEqual(DayOfWeek.Tuesday)).ShouldContain(
            ((int)DayOfWeek.Tuesday).ToString());
        ((string)e.Column(x => x.Col).LessThan(x => x.Other)).ShouldContain("< ");
        ((string)cc.RawColumn<int>("Col").LessThan(2)).ShouldContain("< 2");
        ((string)cc.RawColumn<DayOfWeek>("Col").LessThan(DayOfWeek.Wednesday)).ShouldContain(((int)DayOfWeek.Wednesday)
            .ToString());
        ((string)e.Column(x => x.Col).LessThanOrEqual(x => x.Other)).ShouldContain("<=");
        ((string)cc.RawColumn<int>("Col").LessThanOrEqual(3)).ShouldContain("<= 3");
        ((string)cc.RawColumn<DayOfWeek>("Col").LessThanOrEqual(DayOfWeek.Thursday)).ShouldContain(
            ((int)DayOfWeek.Thursday).ToString());
    }

    [Fact]
    public void Between_NotBetween_Should_Handle_All_Types()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };
        ((string)cc.RawColumn<string>("Col").Between("a", "b")).ShouldContain("BETWEEN");
        ((string)cc.RawColumn<int>("Col").Between(1, 2)).ShouldContain("BETWEEN");
        ((string)cc.RawColumn<double>("Col").Between(1.1, 2.2)).ShouldContain("BETWEEN");
        ((string)cc.RawColumn<string>("Col").NotBetween("a", "b")).ShouldContain("NOT BETWEEN");
        ((string)cc.RawColumn<int>("Col").NotBetween(1, 2)).ShouldContain("NOT BETWEEN");
        ((string)cc.RawColumn<double>("Col").NotBetween(1.1, 2.2)).ShouldContain("NOT BETWEEN");
    }

    [Fact]
    public void IsNull_IsNotNull_Should_Work()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };
        ((string)cc.RawColumn<string?>("Col").IsNull()).ShouldContain("IS NULL");
        ((string)cc.RawColumn<string?>("Col").IsNotNull()).ShouldContain("IS NOT NULL");
    }

    [Fact]
    public void In_And_NotIn_Should_Throw_On_Empty_Collections()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };

        Should.Throw<ArgumentException>(() => cc.RawColumn<int>("Col").In(Array.Empty<int>()));
        Should.Throw<ArgumentException>(() => cc.RawColumn<string>("Col").In(Array.Empty<string>()));
        Should.Throw<ArgumentException>(() => cc.RawColumn<DayOfWeek>("Col").In(Array.Empty<DayOfWeek>()));
        Should.Throw<ArgumentException>(() => cc.RawColumn<int>("Col").NotIn(Array.Empty<int>()));
        Should.Throw<ArgumentException>(() => cc.RawColumn<string>("Col").NotIn(Array.Empty<string>()));
        Should.Throw<ArgumentException>(() => cc.RawColumn<DayOfWeek>("Col").NotIn(Array.Empty<DayOfWeek>()));
    }

    [Fact]
    public void DelimitString_Should_Use_Correct_Symbols()
    {
        var ccPg = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };
        var ccMy = new SqlCheckConstraintBuilder
        { Dialect = SqlDialect.MySql, IdentifierCase = SqlIdentifierCase.PascalCase, DelimitIdentifiers = true };
        var ccSql = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };
        var ccOra = new SqlCheckConstraintBuilder
        { Dialect = SqlDialect.Oracle, IdentifierCase = SqlIdentifierCase.PascalCase, DelimitIdentifiers = true };
        var ccLite = new SqlCheckConstraintBuilder
        { Dialect = SqlDialect.Sqlite, IdentifierCase = SqlIdentifierCase.PascalCase, DelimitIdentifiers = true };

        ((string)ccPg.RawColumn<string?>("Col").IsNull()).ShouldContain("""
                                                                        "Col"
                                                                        """);
        ((string)ccMy.RawColumn<string?>("Col").IsNull()).ShouldContain("`Col`");
        ((string)ccSql.RawColumn<string?>("Col").IsNull()).ShouldContain("[Col]");
        ((string)ccOra.RawColumn<string?>("Col").IsNull()).ShouldContain("""
                                                                         "Col"
                                                                         """);
        ((string)ccLite.RawColumn<string?>("Col").IsNull()).ShouldContain("""
                                                                          "Col"
                                                                          """);
    }

    [Fact]
    public void TransformCase_Should_Respect_IdentifierCase()
    {
        var ccLower = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };
        var ccUpper = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };
        var ccPascal = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };

        ((string)ccUpper.For<ParityEntity>().Column(x => x.TestColumn).In(1)).ShouldContain("TEST_COLUMN");
        ((string)ccPascal.For<ParityEntity>().Column(x => x.test_column).In(1)).ShouldContain("TestColumn");
        ((string)ccLower.For<ParityEntity>().Column(x => x.TestColumn).In(1)).ShouldContain("test_column");
    }

    [Fact]
    public void SqlString_Should_Escape_Single_Quotes()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };
        ((string)cc.RawColumn<string>("Col").EqualTo("O'Reilly")).ShouldContain("'O''Reilly'");
    }

    [Fact]
    public void LengthOperatorHandler_Should_Apply_For_Text_Types()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.SqlServer,
            IdentifierCase = SqlIdentifierCase.PascalCase,
            DelimitIdentifiers = true
        };
        ((string)cc.RawColumn<string>("Col").Length().EqualTo(5)).ShouldContain("LEN(");
        ((string)cc.RawColumn<string>("Col").Length().NotEqualTo(5)).ShouldContain("LEN(");
    }

    [Fact]
    public void LengthOperatorHandler_Oracle_Should_Use_Length()
    {
        var cc = new SqlCheckConstraintBuilder
        { Dialect = SqlDialect.Oracle, IdentifierCase = SqlIdentifierCase.PascalCase, DelimitIdentifiers = true };
        var e = cc.For<ParityEntity>();
        ((string)e.Column(x => x.Name).Length().EqualTo(5)).ShouldContain("LENGTH(");
        ((string)e.Column(x => x.Name).Length().NotEqualTo(5)).ShouldContain("LENGTH(");
    }

    [Fact]
    public void Oracle_UpperSnakeCase_Should_Delimit_With_Double_Quotes()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };
        cc.For<ParityEntity>().Column(x => x.IsActive).EqualTo(1).ShouldRender("""
                                                                               "IS_ACTIVE" = 1
                                                                               """);
    }

    [Fact]
    public void Oracle_GreaterThan_String_Should_Use_Length()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };
        cc.RawColumn<string>("SELL_PRICE").Length().GreaterThan(100).ShouldRender("""
            LENGTH("SELL_PRICE") > 100
            """);
    }

    [Fact]
    public void Oracle_Flag_Column_As_Int_Should_Render_Numeric_Literals()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };
        var e = cc.For<ParityEntity>();

        e.Column(x => x.IsActive).EqualTo(1).ShouldRender("IS_ACTIVE = 1");
        e.Column(x => x.IsActive).NotEqualTo(0).ShouldRender("IS_ACTIVE <> 0");
    }

    [Fact]
    public void Oracle_Flag_Column_As_Char_Should_Render_Quoted_Literals()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };
        var e = cc.For<ParityEntity>();

        e.Column(x => x.IsCash).EqualTo("Y").ShouldRender("IS_CASH = 'Y'");
        e.Column(x => x.IsCash).EqualTo("N").ShouldRender("IS_CASH = 'N'");
    }

    [Fact]
    public void Oracle_Between_With_Method_Delimit_Override()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };

        cc.For<ParityEntity>().Column(x => x.BuyPrice).WithDelimiter().Between(90, 100).ShouldRender("""
            "BUY_PRICE" BETWEEN 90 AND 100
            """);
    }

    [Fact]
    public void Oracle_AndOr_FourWay_AccountHead_Flags()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };
        var ah = cc.For<AccountHead>();

        const string sql =
            "((IS_CASH = 1 AND IS_BANK = 0 AND IS_MOBILE_BANK = 0) OR (IS_CASH = 0 AND IS_BANK = 1 AND IS_MOBILE_BANK = 0) OR (IS_CASH = 0 AND IS_BANK = 0 AND IS_MOBILE_BANK = 1) OR (IS_CASH = 0 AND IS_BANK = 0 AND IS_MOBILE_BANK = 0))";

        string testSql = cc.Or(
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(1),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(0)),
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(1),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(0)),
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(1)),
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(0))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_AndOr_Verified_Phone_Otp()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };
        var e = cc.For<ParityEntity>();

        const string sql =
            "((IS_VERIFIED = 0 AND PHONE IS NULL AND OTP IS NULL) OR (IS_VERIFIED = 1 AND PHONE IS NOT NULL AND OTP IS NOT NULL))";

        string testSql = cc.Or(
            cc.And(e.Column(x => x.IsVerified).EqualTo(0), e.Column(x => x.Phone).IsNull(),
                e.Column(x => x.Otp).IsNull()),
            cc.And(e.Column(x => x.IsVerified).EqualTo(1), e.Column(x => x.Phone).IsNotNull(),
                e.Column(x => x.Otp).IsNotNull())
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_AndOr_Debit_Credit_Entry()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };
        var e = cc.For<ParityEntity>();

        const string sql =
            "((CREDIT_AMOUNT >= 0 AND DEBIT_AMOUNT = 0) OR (DEBIT_AMOUNT >= 0 AND CREDIT_AMOUNT = 0))";

        string testSql = cc.Or(
            cc.And(e.Column(x => x.CreditAmount).GreaterThanOrEqual(0), e.Column(x => x.DebitAmount).EqualTo(0)),
            cc.And(e.Column(x => x.DebitAmount).GreaterThanOrEqual(0), e.Column(x => x.CreditAmount).EqualTo(0))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_AndOr_FourWay_With_Delimiters()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };
        var ah = cc.For<AccountHead>();

        const string sql =
            """
            (("IS_CASH" = 1 AND "IS_BANK" = 0 AND "IS_MOBILE_BANK" = 0) OR ("IS_CASH" = 0 AND "IS_BANK" = 1 AND "IS_MOBILE_BANK" = 0) OR ("IS_CASH" = 0 AND "IS_BANK" = 0 AND "IS_MOBILE_BANK" = 1) OR ("IS_CASH" = 0 AND "IS_BANK" = 0 AND "IS_MOBILE_BANK" = 0))
            """;

        string testSql = cc.Or(
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(1),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(0)),
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(1),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(0)),
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(1)),
            cc.And(ah.Column(x => x.IsCash).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsBank).BoolAsNumeric().EqualTo(0),
                ah.Column(x => x.IsMobileBank).BoolAsNumeric().EqualTo(0))
        );

        testSql.ShouldBe(sql);
    }

    [Fact]
    public void Oracle_BoolAsString_GeneratesMappedLiterals()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = false
        };
        var ah = cc.For<AccountHead>();

        ah.Column(x => x.IsCash).BoolAsString().EqualTo("Y").ShouldRender("IS_CASH = 'Y'");
        ah.Column(x => x.IsCash).BoolAsString().EqualTo("N").ShouldRender("IS_CASH = 'N'");
    }

    [Fact]
    public void Case_SingleWhen_End_GeneratesCorrectSql()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.Case()
            .When(cc.RawColumn<string>("status").EqualTo("paid"))
            .Then(cc.RawColumn<decimal>("amount").GreaterThan(0))
            .End()
            .ShouldRender("CASE WHEN status = 'paid' THEN amount > 0 END");
    }

    [Fact]
    public void Case_MultipleWhen_End_GeneratesCorrectSql()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.Case()
            .When(cc.RawColumn<string>("status").EqualTo("paid"))
            .Then(cc.RawColumn<decimal>("amount").GreaterThan(0))
            .When(cc.RawColumn<string>("status").EqualTo("void"))
            .Then(cc.RawColumn<decimal>("amount").EqualTo(0))
            .End()
            .ShouldRender("CASE WHEN status = 'paid' THEN amount > 0 WHEN status = 'void' THEN amount = 0 END");
    }

    [Fact]
    public void Case_EndWithElse_GeneratesCorrectSql()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.Case()
            .When(cc.RawColumn<string>("status").EqualTo("paid"))
            .Then(cc.RawColumn<decimal>("amount").GreaterThan(0))
            .When(cc.RawColumn<string>("status").EqualTo("void"))
            .Then(cc.RawColumn<decimal>("amount").EqualTo(0))
            .EndWithElse(cc.RawColumn<decimal>("amount").GreaterThanOrEqual(0))
            .ShouldRender(
                "CASE WHEN status = 'paid' THEN amount > 0 WHEN status = 'void' THEN amount = 0 ELSE amount >= 0 END");
    }

    [Theory]
    [InlineData(true, "TRUE")]
    [InlineData(false, "FALSE")]
    public void Case_EndWithElse_Boolean_GeneratesCorrectSql(bool elseExpression, string expected)
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        cc.Case()
            .When(cc.RawColumn<string>("status").EqualTo("paid"))
            .Then(cc.RawColumn<int>("amount").EqualTo(1))
            .EndWithElse(elseExpression)
            .ShouldRender($"CASE WHEN status = 'paid' THEN amount = 1 ELSE {expected} END");
    }

    [Fact]
    public void Case_When_NullWhen_Throws()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        Should.Throw<ArgumentNullException>(() =>
            cc.Case().When(null!));
    }

    [Fact]
    public void Case_When_NullThen_Throws()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        Should.Throw<ArgumentNullException>(() =>
            cc.Case().When(cc.RawPredicate("status = 'paid'")).Then(null!));
    }

    [Fact]
    public void Case_EndWithElse_NullElse_Throws()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = false
        };

        Should.Throw<ArgumentNullException>(() =>
            cc.Case()
                .When(cc.RawColumn<string>("status").EqualTo("paid"))
                .Then(cc.RawColumn<decimal>("amount").GreaterThan(0))
                .EndWithElse(null!));
    }
}