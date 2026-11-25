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
        Assert.Equal("\"DISCOUNT\" <= (CASE WHEN \"IS_VIP\" = 1 THEN 50 ELSE 10 END)", byName["ck_orders_discount"]);
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
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .ConfigureSqlExpressionBuilder(cc => cc
                .UseDelimitedIdentifiers(false)
                .UseNestedColumnSeparator("__"));

        var options = optionsBuilder.Build();
        var registry = new GlobalOverrideRegistry();

        var schema = SchemaScanner.ScanRegistry(
            registry,
            options.Dialect,
            options.IdentifierCase,
            [registry],
            sqlExpressionOptions: options.SqlExpressionOptions);

        var byName = schema.Tables.Single().CheckConstraints.ToDictionary(c => c.Name, c => c.Sql);

        Assert.Equal("shipping__zip_code IS NOT NULL", byName["ck_inherits_global"]);
        Assert.Equal("\"shipping__zip_code\" IS NOT NULL", byName["ck_partial_override"]);
    }

    private sealed class ComputedOrder
    {
        public int Id { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal FullTotal { get; set; }
    }

    private sealed class ComputedColumnRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<ComputedOrder>(b =>
            {
                b.ToTable("computed_orders");
                b.Property(x => x.FullTotal)
                    .HasPrecision(18, 2)
                    .HasComputedColumnSql(cc => cc.Column(x => x.Subtotal).Math().Plus(x => x.Tax))
                    .IsStored();
            });
        }
    }

    [Fact]
    public void HasComputedColumnSql_TypedExpression_RendersForDialectAndIdentifierCase()
    {
        var registry = new ComputedColumnRegistry();

        var pgSchema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);
        var pgCol = pgSchema.Tables.Single().Columns.Single(c => c.PropertyName == nameof(ComputedOrder.FullTotal));
        Assert.Equal("\"subtotal\" + \"tax\"", pgCol.ComputedColumnSql);
        Assert.True(pgCol.IsComputedStored);

        var oracleSchema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.Oracle,
            SqlIdentifierCase.UpperSnakeCase,
            [registry]);
        var oracleCol = oracleSchema.Tables.Single().Columns.Single(c => c.PropertyName == nameof(ComputedOrder.FullTotal));
        Assert.Equal("\"SUBTOTAL\" + \"TAX\"", oracleCol.ComputedColumnSql);
        Assert.True(oracleCol.IsComputedStored);
    }

    private sealed class CustomNamedOrder
    {
        public int Id { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal FullTotal { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class CustomNamedRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<CustomNamedOrder>(b =>
            {
                b.ToTable("custom_orders");
                b.Property(x => x.Subtotal).HasColumnName("net_amt");
                b.Property(x => x.Tax).HasColumnName("vat_amt");
                b.Property(x => x.IsDeleted).HasColumnName("deleted_flag");
                b.Property(x => x.FullTotal)
                    .HasComputedColumnSql(cc => cc.Column(x => x.Subtotal).Math().Plus(x => x.Tax));
                b.HasCheckConstraint("ck_net_positive", cc => cc.Column(x => x.Subtotal).GreaterThanOrEqual(0));
                b.HasIndex(x => x.Subtotal)
                    .HasFilter(cc => cc.Column(x => x.IsDeleted).EqualTo(false));
            });
        }
    }

    [Fact]
    public void TypedSqlBuilders_RespectCustomHasColumnName()
    {
        var registry = new CustomNamedRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var table = schema.Tables.Single();
        var fullTotal = table.Columns.Single(c => c.PropertyName == nameof(CustomNamedOrder.FullTotal));
        Assert.Equal("\"net_amt\" + \"vat_amt\"", fullTotal.ComputedColumnSql);
        Assert.False(fullTotal.IsComputedStored);

        var check = Assert.Single(table.CheckConstraints);
        Assert.Equal("\"net_amt\" >= 0", check.Sql);

        var index = Assert.Single(table.Indexes);
        Assert.Equal("\"deleted_flag\" = FALSE", index.FilterSql);

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        Assert.Contains("GENERATED ALWAYS AS (\"net_amt\" + \"vat_amt\") VIRTUAL", diff.SqlScript);
    }
}
