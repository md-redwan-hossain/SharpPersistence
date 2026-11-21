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
        bool delimitIdentifiers = true)
    {
        return new SqlCheckConstraintBuilder
        {
            Dialect = dialect,
            IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
            DelimitIdentifiers = delimitIdentifiers
        };
    }

    [Theory]
    [InlineData(true, """CASE WHEN "is_override_allowed" = 0 THEN "is_overridable" = 0 ELSE 1 END""")]
    [InlineData(false, "CASE WHEN is_override_allowed = 0 THEN is_overridable = 0 ELSE 1 END")]
    public void Case_WithNumericElse_GeneratesExpectedSql(bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        var sql = cc.Case()
            .When(
                condition: policy.Column(x => x.IsOverrideAllowed).EqualTo(0),
                then: policy.Column(x => x.IsOverridable).EqualTo(0))
            .EndWithElse(1)
            .Build();

        sql.ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, """
        "is_override_allowed" = 0
        """)]
    [InlineData(false, "is_override_allowed = 0")]
    public void ImplicitStringConversion_WorksWithoutCallingBuild(bool delimitIdentifiers, string expected)
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
    [InlineData(SqlDialect.PostgreSql, false, "length(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.SqlServer, false, "LEN(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.MySql, false, "CHAR_LENGTH(code) BETWEEN 3 AND 50")]
    [InlineData(SqlDialect.Oracle, false, "LENGTH(code) BETWEEN 3 AND 50")]
    public void Length_Between_RespectsDialect(SqlDialect dialect, bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(dialect, delimitIdentifiers);
        var policy = cc.For<AppPolicy>();

        var sql = policy.Column(x => x.Code).Length().Between(3, 50).Build();

        sql.ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, """("unit_price" - "discount") * "quantity" > 0""")]
    [InlineData(false, "(unit_price - discount) * quantity > 0")]
    public void Math_PreservesOperatorPrecedence(bool delimitIdentifiers, string expected)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var line = cc.For<InvoiceLine>();

        var sql = line.Column(x => x.UnitPrice)
            .Math()
            .Minus(x => x.Discount)
            .MultiplyBy(x => x.Quantity)
            .GreaterThan(0)
            .Build();

        sql.ShouldBe(expected);
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

        var sql = line.Column(x => x.Balance)
            .Math()
            .Plus(x => x.Remaining)
            .Minus(x => x.Tax)
            .EqualTo(50)
            .Build();

        sql.ShouldBe(expected);
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

        shipment.Column(x => x.Status).In(TestStatus.Draft, TestStatus.Active).Build()
            .ShouldBe(expectedIn);

        shipment.Column(x => x.Status).NotIn(TestStatus.Archived).Build()
            .ShouldBe(expectedNotIn);
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

        var sql = cc.Or(
            shipment.Column(x => x.Status).NotEqualTo(TestStatus.Active),
            shipment.Column(x => x.DispatchedAt).IsNotNull()
        ).Build();

        sql.ShouldBe(expectedOr);

        shipment.Column(x => x.BillingCountry).EqualToColumn(x => x.ShippingCountry).Build()
            .ShouldBe(expectedColEq);
    }

    [Theory]
    [InlineData(true, """length("address_country") = 2""", """length("country") = 2""")]
    [InlineData(false, "length(address_country) = 2", "length(country) = 2")]
    public void NestedProperty_SupportsJoinedAndLeafNaming(
        bool delimitIdentifiers,
        string expectedJoined,
        string expectedLeaf)
    {
        var cc = CreateBuilder(delimitIdentifiers: delimitIdentifiers);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.Address.Country).Length().EqualTo(2).Build()
            .ShouldBe(expectedJoined);

        shipment.Column(x => x.Address.Country, includeParentNames: false).Length().EqualTo(2).Build()
            .ShouldBe(expectedLeaf);
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

        policy.Column(x => x.IsActive).EqualTo(true).Build()
            .ShouldBe(expectedBool);

        cc.RawColumn<Guid>("tenant_id").IsNotNull().Build()
            .ShouldBe(expectedFallback);
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

        policy.Column(x => x.IsActive).EqualTo(true).Build().ShouldBe(expectedTrue);
        policy.Column(x => x.IsActive).EqualTo(false).Build().ShouldBe(expectedFalse);
    }

    [Fact]
    public void EqualToNull_And_NotEqualToNull_GenerateIsNullAndIsNotNull()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.DispatchedAt).EqualTo(null).Build().ShouldBe("dispatched_at IS NULL");
        shipment.Column(x => x.DispatchedAt).NotEqualTo(null).Build().ShouldBe("dispatched_at IS NOT NULL");
    }

    [Fact]
    public void TemporalLiterals_AreFormattedAsQuotedIsoStrings()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var shipment = cc.For<Shipment>();

        shipment.Column(x => x.DispatchedAt)
            .GreaterThanOrEqual(new DateTime(2026, 1, 15, 8, 30, 0))
            .Build()
            .ShouldBe("dispatched_at >= '2026-01-15 08:30:00'");

        cc.RawColumn<DateOnly>("start_date")
            .GreaterThan(new DateOnly(2026, 1, 1))
            .Build()
            .ShouldBe("start_date > '2026-01-01'");
    }

    [Fact]
    public void TypedShadowColumn_SupportsLengthAndMath()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);

        cc.RawColumn<string>("code").Length().EqualTo(5).Build().ShouldBe("length(code) = 5");
        cc.RawColumn<decimal>("price").Math().GreaterThan(0).Build().ShouldBe("price > 0");
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
            .Build()
            .ShouldBe("unit_price - discount > tax");
    }

    [Fact]
    public void In_WithNullElement_ThrowsArgumentException()
    {
        var cc = CreateBuilder(delimitIdentifiers: false);
        var policy = cc.For<AppPolicy>();

        Should.Throw<ArgumentException>(() => policy.Column(x => x.Code).In("A", null!));
    }
}
