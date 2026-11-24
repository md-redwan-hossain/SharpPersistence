using SharpPersistence.Migrator.EfCore;

namespace SharpPersistence.Migrator.Tests;

public sealed class EfCoreConfigTranslatorTests
{
    [Fact]
    public void TranslateSource_RewritesInterfaceBuilderToTableForeignKeysPostgresMethodAndFlagsEfOnlyCalls()
    {
        const string efSource = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Demo.Infrastructure.Configurations;

            public class OrderConfiguration : IEntityTypeConfiguration<Order>
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.ToTable("orders", t =>
                    {
                        t.HasComment("Customer orders");
                        t.HasCheckConstraint("ck_orders_total", "total >= 0");
                    });

                    builder.HasKey(x => x.Id).HasName("pk_orders");
                    builder.Property(x => x.Code).HasMaxLength(32).IsRequired();

                    builder.HasIndex(x => x.Code)
                        .HasDatabaseName("ix_orders_code")
                        .HasMethod("gin");

                    builder.HasOne<User>()
                        .WithMany()
                        .HasForeignKey(x => x.UserId)
                        .HasPrincipalKey(u => u.Id)
                        .HasConstraintName("fk_orders_users_user_id")
                        .OnDelete(DeleteBehavior.Restrict);

                    builder.HasOne<Invoice>()
                        .WithOne()
                        .HasForeignKey<Order>(x => x.InvoiceId)
                        .OnDelete(DeleteBehavior.Cascade);

                    builder.OwnsOne(x => x.ShippingAddress);
                }
            }
            """;

        var result = EfCoreConfigTranslator.TranslateSource(efSource, "OrderConfiguration.cs");

        Assert.NotNull(result);
        Assert.Equal("Order", result.EntityTypeName);

        var code = result.TranslatedSource;
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", code);
        Assert.Contains("using SharpPersistence.Migrator.Abstractions;", code);
        Assert.Contains("using SharpPersistence.Migrator.Builders;", code);
        Assert.Contains("using SharpPersistence.Migrator.Attributes;", code);
        Assert.Contains("using SharpPersistence.Migrator.Enums;", code);
        Assert.Contains("using SharpPersistence.Migrator.PostgreSql;", code);

        Assert.Contains("[EntityConfiguration<Order>]", code);
        Assert.Contains("public sealed class OrderConfiguration : IEntityConfiguration<Order>", code);
        Assert.Contains("public void Configure(EntityBuilder<Order> builder)", code);

        Assert.Contains("builder.ToTable(\"orders\");", code);
        Assert.Contains("builder.HasComment(\"Customer orders\");", code);
        Assert.Contains("builder.HasCheckConstraint(\"ck_orders_total\", \"total >= 0\");", code);
        Assert.Contains(".HasMethod(PostgresIndexMethod.Gin)", code);

        Assert.Contains("builder.HasForeignKey(x => x.UserId)", code);
        Assert.Contains(".References<User>(u => u.Id)", code);
        Assert.Contains(".HasConstraintName(\"fk_orders_users_user_id\")", code);
        Assert.Contains(".OnDelete(DbReferentialAction.Restrict)", code);

        Assert.Contains("builder.HasForeignKey(x => x.InvoiceId)", code);
        Assert.Contains(".References<Invoice>(p => p.Id)", code);
        Assert.Contains(".IsOneToOne()", code);
        Assert.Contains(".OnDelete(DbReferentialAction.Cascade)", code);

        Assert.Contains("// TODO(SharpPersistence.Migrator): EF Core-specific call 'OwnsOne' requires manual review.", code);
        Assert.Single(result.Warnings, w => w.Contains("OwnsOne", StringComparison.Ordinal));
    }

    [Fact]
    public void TranslateSource_NonConfigFile_ReturnsNull()
    {
        const string plainSource = """
            namespace Demo;
            public sealed class Order { public Guid Id { get; set; } }
            """;

        var result = EfCoreConfigTranslator.TranslateSource(plainSource, "Order.cs");
        Assert.Null(result);
    }

    [Fact]
    public void TranslateSource_FlagsEfOnlyCallsInsideToTableCallbackAndAddsEfCoreUsingForGenericHasConversion()
    {
        const string efSource = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Demo;

            public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
            {
                public void Configure(EntityTypeBuilder<Invoice> builder)
                {
                    builder.ToTable("invoices", t =>
                    {
                        t.HasTrigger("tr_invoices_audit");
                    });
                    builder.Property(x => x.Status).HasConversion<string>();
                }
            }
            """;

        var result = EfCoreConfigTranslator.TranslateSource(efSource, "InvoiceConfiguration.cs");

        Assert.NotNull(result);
        Assert.Contains("using SharpPersistence.Migrator.EfCore;", result.TranslatedSource);
        Assert.Contains("// TODO(SharpPersistence.Migrator): EF Core-specific call 'HasTrigger' requires manual review.", result.TranslatedSource);
        Assert.Single(result.Warnings, w => w.Contains("HasTrigger", StringComparison.Ordinal));
    }
}
