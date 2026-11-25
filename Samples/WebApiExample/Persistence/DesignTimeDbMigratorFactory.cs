using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using WebApiExample.Entities;

namespace WebApiExample.Persistence;

public sealed class WebApiExampleRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry)
    {
        registry.HasDefaultSchema("public");

        registry.Entity<Category>(b =>
        {
            b.ToTable("categories")
                .HasComment("Product catalog categories");
            b.Property(x => x.Name)
                .HasMaxLength(120)
                .HasComment("Unique category display name");
            b.Property(x => x.Description)
                .HasMaxLength(500);
            b.HasIndex(x => x.Name)
                .IsUnique()
                .HasDatabaseName("ux_categories_name");
        });

        registry.Entity<Product>(b =>
        {
            b.ToTable("products")
                .HasComment("Catalog products linked to categories");
            b.Property(x => x.Name)
                .HasMaxLength(200);
            b.Property(x => x.Sku)
                .HasMaxLength(64)
                .HasComment("Unique stock keeping unit");
            b.Property(x => x.Price)
                .HasPrecision(18, 2);
            b.HasIndex(x => x.Sku)
                .IsUnique()
                .HasDatabaseName("ux_products_sku");
            b.HasIndex(x => x.CategoryId)
                .HasDatabaseName("ix_products_category_id");
            b.HasForeignKey(x => x.CategoryId)
                .References<Category>(c => c.Id)
                .OnDelete(DbReferentialAction.Restrict);
            b.HasCheckConstraint("ck_products_price_non_negative", expr => expr.Column(x => x.Price).GreaterThanOrEqual(0m));
            b.HasCheckConstraint("ck_products_stock_non_negative", expr => expr.Column(x => x.StockQuantity).GreaterThanOrEqual(0));
        });
    }
}

public sealed class DesignTimeDbMigratorFactory : IDesignTimeDbMigratorFactory
{
    public void Configure(DbMigratorOptionsBuilder options, string[] args)
    {
        options.UsePostgreSql("Host=localhost;Database=webapi_example;Username=postgres;Password=postgres")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .AddRegistry<WebApiExampleRegistry>();
    }
}
