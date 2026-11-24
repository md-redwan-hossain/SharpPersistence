using SharpPersistence.Enums;
using Shouldly;

namespace SharpPersistence.Tests;

public class SqlCheckConstraintBuilderTests
{
    private enum TestStatus
    {
        Draft = 1,
        Active = 2,
        Archived = 3
    }

    private enum LargeStatus : long
    {
        Huge = 5_000_000_000L
    }

    private enum UnsignedLargeStatus : ulong
    {
        Maxed = 10_000_000_000UL
    }

    private sealed class AppPolicy
    {
        public int IsOverrideAllowed { get; set; }
        public int IsOverridable { get; set; }
        public string Code { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    private sealed class InvoiceLine
    {
        public decimal UnitPrice { get; set; }
        public decimal Discount { get; set; }
        public int Quantity { get; set; }
        public decimal Balance { get; set; }
        public decimal Remaining { get; set; }
        public decimal Tax { get; set; }
    }

    private sealed class Address
    {
        public string Country { get; set; } = string.Empty;
    }

    private sealed class Shipment
    {
        public TestStatus Status { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public string BillingCountry { get; set; } = string.Empty;
        public string ShippingCountry { get; set; } = string.Empty;
        public Address Address { get; set; } = new();
    }

    private static SqlCheckConstraintBuilder CreateBuilder(
        SqlDialect dialect = SqlDialect.PostgreSql,
        bool delimitIdentifiers = true) => new()
    {
        Dialect = dialect,
        IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
        DelimitIdentifiers = delimitIdentifiers
    };

    [Theory]
    [InlineData(true, """CASE WHEN "is_override_allowed" = 0 THEN "is_overridable" = 0 ELSE 1 END""")]
    [InlineData(false, "CASE WHEN is_override_allowed = 0 THEN is_overridable = 0 ELSE 1 END")]
    public void Case_WithNumericElse_GeneratesExpectedSql(bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        cc.Case()
            .When(policy.Column(x => x.IsOverrideAllowed).EqualTo(0))
            .Then(policy.Column(x => x.IsOverridable).EqualTo(0))
            .EndWithElse(1)
            .ShouldRender(expected);
    }

    [Theory]
    [InlineData(true, """
                      "is_override_allowed" = 0
                      """)]
    [InlineData(false, "is_override_allowed = 0")]
    public void ImplicitStringConversion_AssignsDirectlyToString(bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        string sql = policy.Column(x => x.IsOverrideAllowed).EqualTo(0);

        sql.ShouldBe(expected);
    }

    [Theory]
    [InlineData(SqlDialect.PostgreSql, true, """length("code") BETWEEN 3 AND 50""")]
    [InlineData(SqlDialect.SqlServer, true, "LEN([code]) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.MySql, true, "CHAR_LENGTH(`code`) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.Oracle, true, """LENGTH("code") BETWEEN 3 AND 50""")]
    [InlineData(SqlDialect.Sqlite, true, """LENGTH("code") BETWEEN 3 AND 50""")]
    [InlineData(SqlDialect.PostgreSql, false, "length(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.SqlServer, false, "LEN(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.MySql, false, "CHAR_LENGTH(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.Oracle, false, "LENGTH(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.Sqlite, false, "LENGTH(code) BETWEEN 3 AND 50")]
    public void Length_Between_RespectsDialect(SqlDialect dialect, bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(dialect, delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.Code).Length().Between(3, 50).ShouldRender(expected);
    }

    [Theory]
    [InlineData(true, """("unit_price" - "discount") * "quantity" > 0""")]
    [InlineData(false, "(unit_price - discount) * quantity > 0")]
    public void Math_PreservesOperatorPrecedence(bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var line = cc.For<InvoiceLine>();

        line.Column(x => x.UnitPrice)
            .Math()
            .Minus(x => x.Discount)
            .MultiplyBy(x => x.Quantity)
            .GreaterThan(0)
            .ShouldRender(expected);
    }

    [Theory]
    [InlineData(true, """
                      "balance" + "remaining" - "tax" = 50
                      """)]
    [InlineData(false, "balance + remaining - tax = 50")]
    public void Math_FlatAdditionAndSubtraction_AvoidsExtraParentheses(bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var line = cc.For<InvoiceLine>();

        line.Column(x => x.Balance)
            .Math()
            .Plus(x => x.Remaining)
            .Minus(x => x.Tax)
            .EqualTo(50)
            .ShouldRender(expected);
    }

    [Theory]
    [InlineData(true, """
                      "status" IN (1, 2)
                      """, """
                           "status" NOT IN (3)
                           """)]
    [InlineData(false, "status IN (1, 2)", "status NOT IN (3)")]
    public void In_And_NotIn_WithEnums_GenerateIntegerList(
        bool delimitIdentifiers,
        string expectedIn,
        string expectedNotIn)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.Status).In(TestStatus.Draft, TestStatus.Active)
            .ShouldRender(expectedIn);

        shipment.Column(x => x.Status).NotIn(TestStatus.Archived)
            .ShouldRender(expectedNotIn);
    }

    [Theory]
    [InlineData(true, """("status" <> 2 OR "dispatched_at" IS NOT NULL)""", """
                                                                            "billing_country" = "shipping_country"
                                                                            """)]
    [InlineData(false, "(status <> 2 OR dispatched_at IS NOT NULL)", "billing_country = shipping_country")]
    public void Or_IsNotNull_And_EqualToColumn_GenerateExpectedSql(
        bool delimitIdentifiers,
        string expectedOr,
        string expectedColEq)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var shipment = cc.For<Shipment>();

        cc.Or(
            shipment.Column(x => x.Status).NotEqualTo(TestStatus.Active),
            shipment.Column(x => x.DispatchedAt).IsNotNull()
        ).ShouldRender(expectedOr);

        shipment.Column(x => x.BillingCountry).EqualTo(x => x.ShippingCountry)
            .ShouldRender(expectedColEq);
    }

    [Theory]
    [InlineData(true, """length("address_country") = 2""", """length("address__country") = 2""")]
    [InlineData(false, "length(address_country) = 2", "length(address__country) = 2")]
    public void NestedColumn_SupportsDefaultAndCustomSeparator(
        bool delimitIdentifiers,
        string expectedDefault,
        string expectedCustom)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var shipment = cc.For<Shipment>();

        var customCc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = delimitIdentifiers,
            NestedSeparator = "__"
        };

        var customShipment = customCc.For<Shipment>();

        shipment.NestedColumn(x => x.Address.Country).Length().EqualTo(2)
            .ShouldRender(expectedDefault);

        customShipment.NestedColumn(x => x.Address.Country).Length().EqualTo(2)
            .ShouldRender(expectedCustom);

        customShipment.Column(x => x.BillingCountry).EqualTo(customShipment.NestedColumn(x => x.Address.Country))
            .ShouldRender(delimitIdentifiers
                ? """
                  "billing_country" = "address__country"
                  """
                : "billing_country = address__country");
    }

    [Fact]
    public void WithDelimiter_And_WithoutDelimiter_OverrideBuilderDefault()
    {
        var cc = CreateBuilder(delimitIdentifiers: true);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.Code).WithoutDelimiter().Length().EqualTo(5)
            .ShouldRender("length(code) = 5");

        var undelimitedCc = CreateBuilder(delimitIdentifiers: false);
        undelimitedCc.For<AppPolicy>().Column(x => x.Code).WithDelimiter().Length().EqualTo(5)
            .ShouldRender("""length("code") = 5""");
    }

    [Fact]
    public void Column_WithNestedExpression_ThrowsArgumentException()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var shipment = cc.For<Shipment>();

        Should.Throw<ArgumentException>(() => shipment.Column(x => x.Address.Country));
        Should.Throw<ArgumentException>(() => shipment.Column(x => x.BillingCountry).EqualTo(x => x.Address.Country));
    }

    [Theory]
    [InlineData(true, """
                      "is_active" = TRUE
                      """, """
                           "tenant_id" IS NOT NULL
                           """)]
    [InlineData(false, "is_active = TRUE", "tenant_id IS NOT NULL")]
    public void Boolean_And_RawStringColumnFallback_GenerateExpectedSql(
        bool delimitIdentifiers,
        string expectedBool,
        string expectedFallback)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.IsActive).EqualTo(true).ShouldRender(expectedBool);
        cc.RawColumn<Guid>("tenant_id").IsNotNull().ShouldRender(expectedFallback);
    }

    [Theory]
    [InlineData(true, "[is_active] = 1", "[is_active] = 0")]
    [InlineData(false, "is_active = 1", "is_active = 0")]
    public void SqlServer_Boolean_GeneratesBitLiterals(
        bool delimitIdentifiers,
        string expectedTrue,
        string expectedFalse)
    {
        var cc = CreateBuilder(SqlDialect.SqlServer, delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.IsActive).EqualTo(true).ShouldRender(expectedTrue);
        policy.Column(x => x.IsActive).EqualTo(false).ShouldRender(expectedFalse);
    }

    [Fact]
    public void EqualToNull_And_NotEqualToNull_GenerateIsNullAndIsNotNull()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.DispatchedAt).EqualTo(null).ShouldRender("dispatched_at IS NULL");
        shipment.Column(x => x.DispatchedAt).NotEqualTo(null).ShouldRender("dispatched_at IS NOT NULL");
    }

    [Fact]
    public void TemporalLiterals_AreFormattedAsQuotedIsoStrings()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.DispatchedAt)
            .GreaterThanOrEqual(new DateTime(2026, 1, 15, 8, 30, 0))
            .ShouldRender("dispatched_at >= '2026-01-15 08:30:00'");

        cc.RawColumn<DateOnly>("start_date")
            .GreaterThan(new DateOnly(2026, 1, 1))
            .ShouldRender("start_date > '2026-01-01'");
    }

    [Fact]
    public void TypedShadowColumn_SupportsLengthAndMath()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);

        cc.RawColumn<string>("code").Length().EqualTo(5).ShouldRender("length(code) = 5");
        cc.RawColumn<decimal>("price").Math().GreaterThan(0).ShouldRender("price > 0");
    }

    [Fact]
    public void Math_ColumnComparison_GeneratesExpectedSql()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var line = cc.For<InvoiceLine>();

        line.Column(x => x.UnitPrice)
            .Math()
            .Minus(x => x.Discount)
            .GreaterThan(x => x.Tax)
            .ShouldRender("unit_price - discount > tax");
    }

    [Fact]
    public void In_WithNullElement_ThrowsArgumentException()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var policy = cc.For<AppPolicy>();

        Should.Throw<ArgumentException>(() => policy.Column(x => x.Code).In("A", null!));
    }

    [Fact]
    public void StringHelpers_Like_Trim_Lower_Upper_GenerateExpectedSql()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.Code).Like("POL-%").ShouldRender("code LIKE 'POL-%'");
        policy.Column(x => x.Code).NotLike("% %").ShouldRender("code NOT LIKE '% %'");
        policy.Column(x => x.Code).Trim().Length().GreaterThan(0).ShouldRender("length(TRIM(code)) > 0");
        policy.Column(x => x.Code).Lower().EqualTo("abc").ShouldRender("LOWER(code) = 'abc'");
        policy.Column(x => x.Code).Upper().EqualTo("ABC").ShouldRender("UPPER(code) = 'ABC'");
    }

    [Theory]
    [InlineData(SqlDialect.PostgreSql, "length(TRIM(code)) > 0")]
    [InlineData(SqlDialect.SqlServer, "LEN(TRIM(code)) > 0")]
    [InlineData(SqlDialect.MySql, "CHAR_LENGTH(TRIM(code)) > 0")]
    [InlineData(SqlDialect.Sqlite, "LENGTH(TRIM(code)) > 0")]
    public void Trim_Length_GreaterThanZero_RespectsDialect(SqlDialect dialect, string expected)
    {
        var cc = CreateBuilder(dialect, delimitIdentifiers: false);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.Code).Trim().Length().GreaterThan(0).ShouldRender(expected);
    }

    [Fact]
    public void Trim_IsNotNull_ForOracle_GeneratesExpectedSql()
    {
        var cc = CreateBuilder(SqlDialect.Oracle, delimitIdentifiers: false);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.Code).Trim().IsNotNull().ShouldRender("TRIM(code) IS NOT NULL");
    }

    [Fact]
    public void Coalesce_BetweenColumns_And_CaseRange_GenerateExpectedSql()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var line = cc.For<InvoiceLine>();

        line.Column(x => x.Discount).Coalesce(0).LessThanOrEqual(x => x.UnitPrice)
            .ShouldRender("COALESCE(discount, 0) <= unit_price");

        line.Column(x => x.Discount).Between(x => x.Tax, x => x.UnitPrice)
            .ShouldRender("discount BETWEEN tax AND unit_price");

        cc.Case()
            .When(line.Column(x => x.Quantity).GreaterThan(10))
            .Then(1)
            .EndWithElse(0)
            .ShouldRender("CASE WHEN quantity > 10 THEN 1 ELSE 0 END");
    }

    [Fact]
    public void Abs_OnMath_GeneratesExpectedSql()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var line = cc.For<InvoiceLine>();

        line.Column(x => x.Balance)
            .Math()
            .Minus(x => x.Remaining)
            .Abs()
            .LessThanOrEqual(0.01m)
            .ShouldRender("ABS(balance - remaining) <= 0.01");
    }

    [Fact]
    public void RawPredicate_ComposesInAndAndCase()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var policy = cc.For<AppPolicy>();

        cc.And(
            policy.Column(x => x.IsActive).EqualTo(true),
            cc.RawPredicate("code ~ '^[A-Z0-9_]+$'")
        ).ShouldRender("(is_active = TRUE AND code ~ '^[A-Z0-9_]+$')");

        cc.Case()
            .When(cc.RawPredicate("is_override_allowed = 0"))
            .Then(1)
            .EndWithElse(0)
            .ShouldRender("CASE WHEN is_override_allowed = 0 THEN 1 ELSE 0 END");
    }

    [Fact]
    public void EnumAsString_And_EnumAsNumeric_FormatEnumLiterals()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.Status).EnumAsString().In(TestStatus.Draft, TestStatus.Active)
            .ShouldRender("status IN ('Draft', 'Active')");

        shipment.Column(x => x.Status).EnumAsString().EqualTo(TestStatus.Archived)
            .ShouldRender("status = 'Archived'");

        shipment.Column(x => x.Status).EnumAsString().EnumAsNumeric().In(TestStatus.Draft, TestStatus.Active)
            .ShouldRender("status IN (1, 2)");
    }

    [Fact]
    public void RawColumn_And_RawColumnComparisons_IgnoreIdentifierCase()
    {
        var cc = new SqlCheckConstraintBuilder
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };

        var policy = cc.For<AppPolicy>();

        cc.RawColumn<string>("myCustom_Col").IsNotNull()
            .ShouldRender("""
                          "myCustom_Col" IS NOT NULL
                          """);

        policy.Column(x => x.Code).EqualToRawColumn("usr_Email")
            .ShouldRender("""
                          "CODE" = "usr_Email"
                          """);
    }

    [Fact]
    public void Coalesce_WithFallbackColumn_GeneratesExpectedSql()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var line = cc.For<InvoiceLine>();

        line.Column(x => x.Discount).Coalesce(x => x.Tax).LessThanOrEqual(x => x.UnitPrice)
            .ShouldRender("COALESCE(discount, tax) <= unit_price");
    }

    [Theory]
    [InlineData(SqlDialect.PostgreSql, true, """(NOT ("is_override_allowed" = FALSE) OR "is_overridable" = FALSE)""")]
    [InlineData(SqlDialect.Oracle, true, """(NOT ("is_override_allowed" = 0) OR "is_overridable" = 0)""")]
    [InlineData(SqlDialect.SqlServer, true, "(NOT ([is_override_allowed] = 0) OR [is_overridable] = 0)")]
    public void If_Then_GeneratesImplicationPredicate(SqlDialect dialect, bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(dialect, delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        if (dialect == SqlDialect.Oracle)
        {
            cc.If(policy.Column(x => x.IsOverrideAllowed).EqualTo(0))
                .Then(policy.Column(x => x.IsOverridable).EqualTo(0))
                .ShouldRender(expected);
        }
        else
        {
            cc.If(policy.Column(x => x.IsActive).EqualTo(false))
                .Then(policy.Column(x => x.IsActive).EqualTo(false))
                .ShouldRender(expected.Replace("is_override_allowed", "is_active")
                    .Replace("is_overridable", "is_active"));
        }
    }

    [Fact]
    public void GenericSqlCheckConstraintBuilder_ComposesColumnsIfAndCase()
    {
        var cc = new SqlCheckConstraintBuilder<InvoiceLine>
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };

        cc.If(cc.Column(x => x.Discount).GreaterThan(0))
            .Then(cc.Column(x => x.UnitPrice).GreaterThan(0))
            .ShouldRender("""(NOT ("discount" > 0) OR "unit_price" > 0)""");

        cc.Column(x => x.Discount).LessThanOrEqual(
            cc.Case()
                .When(cc.Column(x => x.UnitPrice).GreaterThan(100)).Then(50)
                .EndWithElse(10))
            .ShouldRender("""
                          "discount" <= (CASE WHEN "unit_price" > 100 THEN 50 ELSE 10 END)
                          """);

        cc.Case()
            .When(cc.Column(x => x.UnitPrice).GreaterThan(0)).Then(1)
            .EndWithElse(0)
            .EqualTo(1)
            .ShouldRender("""(CASE WHEN "unit_price" > 0 THEN 1 ELSE 0 END) = 1""");
    }

    [Fact]
    public void ColumnRawPredicate_SupportsPostfixAndFormattedLambda()
    {
        var pg = new SqlCheckConstraintBuilder<AppPolicy>
        {
            Dialect = SqlDialect.PostgreSql,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = true
        };

        pg.Column(x => x.Code).RawPredicate("~ '^ORD-[0-9]+$'")
            .ShouldRender("""
                          "code" ~ '^ORD-[0-9]+$'
                          """);

        var oracle = new SqlCheckConstraintBuilder<AppPolicy>
        {
            Dialect = SqlDialect.Oracle,
            IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
            DelimitIdentifiers = true
        };

        oracle.Column(x => x.Code).RawPredicate(col => $"REGEXP_LIKE({col}, '^ORD-[0-9]+$')")
            .ShouldRender("""REGEXP_LIKE("CODE", '^ORD-[0-9]+$')""");
    }

    [Theory]
    [InlineData(true, "\"is_active\" = 1", "\"is_active\" = 0")]
    [InlineData(false, "is_active = 1", "is_active = 0")]
    public void Oracle_Boolean_GeneratesNumericLiterals(
        bool delimitIdentifiers,
        string expectedTrue,
        string expectedFalse)
    {
        var cc = CreateBuilder(SqlDialect.Oracle, delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        policy.Column(x => x.IsActive).EqualTo(true).ShouldRender(expectedTrue);
        policy.Column(x => x.IsActive).EqualTo(false).ShouldRender(expectedFalse);
    }

    [Fact]
    public void LargeInt64AndUInt64Enums_DoNotOverflow()
    {
        var cc = CreateBuilder(SqlDialect.PostgreSql, delimitIdentifiers: false);

        cc.RawColumn<LargeStatus>("status").EqualTo(LargeStatus.Huge)
            .ShouldRender("status = 5000000000");

        cc.RawColumn<UnsignedLargeStatus>("u_status").EqualTo(UnsignedLargeStatus.Maxed)
            .ShouldRender("u_status = 10000000000");
    }
}

internal static class SqlBuiltExpressionTestExtensions
{
    public static void ShouldRender(this SqlBuiltExpression expression, string expected)
        => ((string)expression).ShouldBe(expected);
}