using Microsoft.EntityFrameworkCore;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class OwnsOneTests
{
    private sealed class GeoPoint
    {
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
    }

    private sealed class Address
    {
        public string City { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public string? Note { get; set; }
        public GeoPoint? Coordinates { get; set; }
        public string DisplayLine { get; set; } = string.Empty;
    }

    private sealed class Order
    {
        public long Id { get; set; }
        public Address BillingAddress { get; set; } = new();
        public Address? ShippingAddress { get; set; }
    }

    private sealed class OrderRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Order>(b =>
            {
                b.ToTable("orders");

                b.OwnsOne(x => x.BillingAddress, a =>
                {
                    a.Property(x => x.City).HasMaxLength(100);
                    a.Property(x => x.ZipCode).HasMaxLength(20);
                    a.HasIndex(x => x.ZipCode).HasDatabaseName("ix_orders_billing_zip");
                    a.Ignore(x => x.DisplayLine);
                });

                b.OwnsOne(x => x.ShippingAddress, a =>
                {
                    a.Property(x => x.City).HasColumnName("ship_city").HasMaxLength(100);
                    a.Ignore(x => x.DisplayLine);
                    a.OwnsOne(x => x.Coordinates, g =>
                    {
                        g.Property(x => x.Latitude).HasPrecision(9, 6);
                        g.Property(x => x.Longitude).HasPrecision(9, 6);
                    });
                });
            });
        }
    }

    [Fact]
    public void OwnsOne_MapsDualAndNestedColumns_Nullability_Conventions_AndSql()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .ConfigureProperties<string>(c => c.HasMaxLength(200))
            .AddRegistry<OrderRegistry>();

        var options = optionsBuilder.Build();
        var model = SchemaScanner.ScanRegistries(
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PostgreSql.Enums,
            options.SqlExpressionOptions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityReplacements,
            [.. options.Registries]);

        var order = Assert.Single(model.Tables);
        var cols = order.Columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);

        // Required navigation: non-nullable C# props stay NOT NULL, nullable prop stays NULL
        Assert.Equal("billing_address_city", cols["BillingAddress.City"].ColumnName);
        Assert.False(cols["BillingAddress.City"].IsNullable);
        Assert.Equal(100, cols["BillingAddress.City"].MaxLength);

        Assert.Equal("billing_address_zip_code", cols["BillingAddress.ZipCode"].ColumnName);
        Assert.False(cols["BillingAddress.ZipCode"].IsNullable);

        Assert.Equal("billing_address_note", cols["BillingAddress.Note"].ColumnName);
        Assert.True(cols["BillingAddress.Note"].IsNullable);
        Assert.Equal(200, cols["BillingAddress.Note"].MaxLength); // inherited ConfigureProperties<string>

        // Ignored owned property is excluded
        Assert.DoesNotContain("BillingAddress.DisplayLine", cols.Keys);
        Assert.DoesNotContain("ShippingAddress.DisplayLine", cols.Keys);

        // Optional navigation: all columns default NULL, custom column name honored, nested OwnsOne propagated
        Assert.Equal("ship_city", cols["ShippingAddress.City"].ColumnName);
        Assert.True(cols["ShippingAddress.City"].IsNullable);

        Assert.Equal("shipping_address_zip_code", cols["ShippingAddress.ZipCode"].ColumnName);
        Assert.True(cols["ShippingAddress.ZipCode"].IsNullable);

        Assert.Equal("shipping_address_coordinates_latitude", cols["ShippingAddress.Coordinates.Latitude"].ColumnName);
        Assert.True(cols["ShippingAddress.Coordinates.Latitude"].IsNullable);
        Assert.Equal("numeric(9,6)", cols["ShippingAddress.Coordinates.Latitude"].SqlType);

        // Index on owned property
        var zipIx = Assert.Single(order.Indexes);
        Assert.Equal("ix_orders_billing_zip", zipIx.Name);
        Assert.Equal(["billing_address_zip_code"], zipIx.ColumnNames);

        // SQL generation emits all owned columns and index cleanly
        var diff = StandaloneSchemaDiffer.GenerateDiff(model, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        Assert.Contains("\"billing_address_city\" character varying(100) NOT NULL", diff.SqlScript);
        Assert.Contains("\"ship_city\" character varying(100) NULL", diff.SqlScript);
        Assert.Contains("\"shipping_address_coordinates_latitude\" numeric(9,6) NULL", diff.SqlScript);
        Assert.Contains("\"ix_orders_billing_zip\"", diff.SqlScript);
    }

    private sealed class InvalidOptionalOwnedRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.OwnsOne(x => x.ShippingAddress, a =>
                {
                    a.Property(x => x.City).IsRequired();
                });
            });
        }
    }

    [Fact]
    public void OptionalOwnedNavigation_WithExplicitIsRequired_ThrowsInFinalizer_AndErrorsInDoctor()
    {
        var registry = new InvalidOptionalOwnedRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SchemaScanner.ScanRegistry(
                registry,
                SqlDialect.PostgreSql,
                SqlIdentifierCase.LowerSnakeCase,
                [registry]));

        Assert.Contains("ShippingAddress.City", ex.Message);
        Assert.Contains("must be nullable", ex.Message);

        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .AddRegistry<InvalidOptionalOwnedRegistry>();
        var options = optionsBuilder.Build();
        var dialect = MigrationDialectRegistry.Get(options);
        var validator = new DoctorEntityValidator(options, dialect);

        var tableDef = SchemaScanner.BuildSingleTableDefinition(
            typeof(Order),
            null,
            [obj =>
            {
                var eb = (EntityBuilder<Order>)obj;
                eb.ToTable("orders");
                eb.OwnsOne(x => x.ShippingAddress, a => a.Property(p => p.City).IsRequired());
            }],
            null,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.SqlExpressionOptions);

        var items = new List<DoctorCheckItem>();
        validator.ValidateTable(
            tableDef,
            new Dictionary<Type, (string, TableDefinition)> { [typeof(Order)] = (nameof(InvalidOptionalOwnedRegistry), tableDef) },
            new Dictionary<string, PostgresEnumSchema>(),
            items);

        Assert.Contains(items, i =>
            i.Severity == DoctorSeverity.Error &&
            i.Message.Contains("ShippingAddress.City") &&
            i.Message.Contains("must be nullable"));
    }

    private sealed class TestOwnsOneDbContext(DbContextOptions<TestOwnsOneDbContext> options, DatabaseModel model)
        : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplySchemaToEfCore(model);
        }
    }

    [Fact]
    public void EfCoreModelBridge_AppliesOwnsOne_NestedOwned_AndOwnedIndexes()
    {
        var registry = new OrderRegistry();
        var model = SchemaScanner.ScanRegistries(
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            null,
            null,
            null,
            registry);

        var dbOptions = new DbContextOptionsBuilder<TestOwnsOneDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestOwnsOneDbContext(dbOptions, model);
        var orderEntity = db.Model.FindEntityType(typeof(Order));
        Assert.NotNull(orderEntity);

        var billingNav = orderEntity.FindNavigation(nameof(Order.BillingAddress));
        Assert.NotNull(billingNav);
        Assert.True(billingNav.ForeignKey.IsOwnership);
        Assert.Equal("billing_address_city", billingNav.TargetEntityType.FindProperty(nameof(Address.City))?.GetColumnName());
        Assert.Contains(billingNav.TargetEntityType.GetIndexes(), ix => ix.GetDatabaseName() == "ix_orders_billing_zip");

        var shippingNav = orderEntity.FindNavigation(nameof(Order.ShippingAddress));
        Assert.NotNull(shippingNav);
        Assert.Equal("ship_city", shippingNav.TargetEntityType.FindProperty(nameof(Address.City))?.GetColumnName());

        var geoNav = shippingNav.TargetEntityType.FindNavigation(nameof(Address.Coordinates));
        Assert.NotNull(geoNav);
        Assert.Equal(
            "shipping_address_coordinates_latitude",
            geoNav.TargetEntityType.FindProperty(nameof(GeoPoint.Latitude))?.GetColumnName());
    }
}
