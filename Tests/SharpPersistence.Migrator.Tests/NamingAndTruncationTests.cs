using System.Text;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class NamingAndTruncationTests
{
    [Theory]
    [InlineData("UserProfile", SqlIdentifierCase.LowerSnakeCase, "user_profile")]
    [InlineData("UserProfile", SqlIdentifierCase.UpperSnakeCase, "USER_PROFILE")]
    [InlineData("user_profile", SqlIdentifierCase.PascalCase, "UserProfile")]
    [InlineData("UserProfile", SqlIdentifierCase.CamelCase, "userProfile")]
    [InlineData("UserProfile", SqlIdentifierCase.LowerCase, "userprofile")]
    [InlineData("UserProfile", SqlIdentifierCase.UpperCase, "USERPROFILE")]
    public void Format_RespectsConvention(string input, SqlIdentifierCase convention, string expected)
    {
        Assert.Equal(expected, NamingFormatter.Format(input, convention));
    }

    [Fact]
    public void TruncateIdentifier_Postgres_CapsAt63BytesDeterministically()
    {
        var longName = "fk_very_long_order_line_items_table_customer_shipping_address_identifier";
        var truncated1 = NamingFormatter.TruncateIdentifier(longName, SqlDialect.PostgreSql);
        var truncated2 = NamingFormatter.TruncateIdentifier(longName, SqlDialect.PostgreSql);

        Assert.Equal(truncated1, truncated2);
        Assert.True(Encoding.UTF8.GetByteCount(truncated1) <= 63);
        Assert.Matches("_[0-9a-f]{8}$", truncated1);
    }

    [Fact]
    public void TruncateIdentifier_Oracle_CapsAt128BytesDeterministically()
    {
        var longName = new string('A', 150);
        var truncated = NamingFormatter.TruncateIdentifier(longName, SqlDialect.Oracle);

        Assert.True(Encoding.UTF8.GetByteCount(truncated) <= 128);
        Assert.Matches("_[0-9a-f]{8}$", truncated);
    }

    [Fact]
    public void TruncateIdentifier_ShortName_Unchanged()
    {
        const string name = "pk_users";
        Assert.Equal(name, NamingFormatter.TruncateIdentifier(name, SqlDialect.PostgreSql));
        Assert.Equal(name, NamingFormatter.TruncateIdentifier(name, SqlDialect.Oracle));
    }

    private sealed class OrderCategory
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class ExplicitNameItem
    {
        public int Id { get; set; }
    }

    private sealed class PluralNamingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Map<OrderCategory>();
            registry.Entity<ExplicitNameItem>(e => e.ToTable("custom_singular"));
        }
    }

    [Fact]
    public void UseNamingConvention_And_PluralizeTableNames_ApplyGloballyWithoutOverridingExplicitToTable()
    {
        var builder = new MigrationManagerOptionsBuilder();
        builder.UsePostgreSql("Host=localhost;Database=test");
        builder.UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .PluralizeTableNames();

        var options = builder.Build();
        var reg = new PluralNamingRegistry();
        var schema = SchemaScanner.ScanRegistry(
            reg,
            options.Dialect,
            options.IdentifierCase,
            [reg],
            pluralizeTableNames: options.PluralizeTableNames);

        var catTable = schema.Tables.Single(t => t.EntityName.EndsWith(nameof(OrderCategory), StringComparison.Ordinal));
        Assert.Equal("order_categories", catTable.TableName);
        Assert.Contains(catTable.Columns, c => c.ColumnName == "display_name");

        var explicitTable = schema.Tables.Single(t => t.EntityName.EndsWith(nameof(ExplicitNameItem), StringComparison.Ordinal));
        Assert.Equal("custom_singular", explicitTable.TableName);
    }
}
