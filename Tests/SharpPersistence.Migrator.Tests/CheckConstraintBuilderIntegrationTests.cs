using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class CheckConstraintBuilderIntegrationTests
{
    private enum OrderStatus
    {
        Pending = 1,
        Shipped = 2
    }

    private sealed class ShippingAddress
    {
        public string ZipCode { get; set; } = string.Empty;
    }

    private sealed class Order
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Discount { get; set; }
        public bool IsVip { get; set; }
        public OrderStatus Status { get; set; }
        public DateTimeOffset? ShippedAt { get; set; }
        public ShippingAddress Shipping { get; set; } = new();
    }

    private sealed class OrderRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Order>(b =>
            {
                b.ToTable("orders");

                b.HasCheckConstraint(
                    "ck_orders_total",
                    cc => cc.Column(x => x.Total).GreaterThanOrEqual(0));

                b.HasCheckConstraint(
                        "ck_orders_shipping_zip",
                        cc => cc.NestedColumn(x => x.Shipping.ZipCode).IsNotNull())
                    .UseDelimitedIdentifiers(false)
                    .UseNestedColumnSeparator("__");

                b.HasCheckConstraint(
                    "ck_orders_discount",
                    cc => cc.Column(x => x.Discount).LessThanOrEqual(
                        cc.Case()
                            .When(cc.Column(x => x.IsVip).EqualTo(true)).Then(50)
                            .EndWithElse(10)));

                b.HasCheckConstraint(
                    "ck_orders_shipped",
                    cc => cc.If(cc.Column(x => x.Status).EqualTo(OrderStatus.Shipped))
                        .Then(cc.Column(x => x.ShippedAt).IsNotNull()));

                b.HasCheckConstraint(
                    "ck_orders_code",
                    cc => cc.Column(x => x.Code).RawPredicate("~ '^ORD-[0-9]+$'"));

                b.HasCheckConstraint("ck_orders_raw", "total >= discount");
            });
        }
    }

    [Fact]
    public void ScanAndDiff_Postgres_RendersTypedCaseIfRawPredicateAndRawStringConstraints()
    {
        var registry = new OrderRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var order = schema.Tables.Single(t => t.TableName == "orders");
        var byName = order.CheckConstraints.ToDictionary(c => c.Name, c => c.Sql);

        Assert.Equal("\"total\" >= 0", byName["ck_orders_total"]);
        Assert.Equal("shipping__zip_code IS NOT NULL", byName["ck_orders_shipping_zip"]);
        Assert.Equal("\"discount\" <= (CASE WHEN \"is_vip\" = TRUE THEN 50 ELSE 10 END)", byName["ck_orders_discount"]);
        Assert.Equal("(NOT (\"status\" = 2) OR \"shipped_at\" IS NOT NULL)", byName["ck_orders_shipped"]);
        Assert.Equal("\"code\" ~ '^ORD-[0-9]+$'", byName["ck_orders_code"]);
        Assert.Equal("total >= discount", byName["ck_orders_raw"]);

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        Assert.Contains("CHECK (\"discount\" <= (CASE WHEN \"is_vip\" = TRUE THEN 50 ELSE 10 END))", diff.SqlScript);
        Assert.Contains("CHECK (shipping__zip_code IS NOT NULL)", diff.SqlScript);
    }

    [Fact]
    public void ScanAndDiff_Oracle_AdaptsDialectAndIdentifierCaseAutomatically()
    {
        var registry = new OrderRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.Oracle,
            SqlIdentifierCase.UpperSnakeCase,
            [registry]);

        var order = schema.Tables.Single(t => t.TableName == "orders");
        var byName = order.CheckConstraints.ToDictionary(c => c.Name, c => c.Sql);

        Assert.Equal("\"TOTAL\" >= 0", byName["ck_orders_total"]);
        Assert.Equal("SHIPPING__ZIP_CODE IS NOT NULL", byName["ck_orders_shipping_zip"]);
        Assert.Equal("\"DISCOUNT\" <= (CASE WHEN \"IS_VIP\" = TRUE THEN 50 ELSE 10 END)", byName["ck_orders_discount"]);
    }

    private sealed class GlobalOverrideRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Order>(b =>
            {
                b.ToTable("orders");

                b.HasCheckConstraint(
                    "ck_inherits_global",
                    cc => cc.NestedColumn(x => x.Shipping.ZipCode).IsNotNull());

                b.HasCheckConstraint(
                        "ck_partial_override",
                        cc => cc.NestedColumn(x => x.Shipping.ZipCode).IsNotNull())
                    .UseDelimitedIdentifiers(true);
            });
        }
    }

    [Fact]
    public void ConfigureCheckConstraintBuilder_AppliesGlobalDefaultsAndAllowsPartialPerConstraintOverride()
    {
        var optionsBuilder = new MigrationManagerOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .ConfigureCheckConstraintBuilder(cc => cc
                .UseDelimitedIdentifiers(false)
                .UseNestedColumnSeparator("__"));

        var options = optionsBuilder.Build();
        var registry = new GlobalOverrideRegistry();

        var schema = SchemaScanner.ScanRegistry(
            registry,
            options.Dialect,
            options.IdentifierCase,
            [registry],
            checkConstraintOptions: options.CheckConstraintOptions);

        var byName = schema.Tables.Single().CheckConstraints.ToDictionary(c => c.Name, c => c.Sql);

        Assert.Equal("shipping__zip_code IS NOT NULL", byName["ck_inherits_global"]);
        Assert.Equal("\"shipping__zip_code\" IS NOT NULL", byName["ck_partial_override"]);
    }
}
