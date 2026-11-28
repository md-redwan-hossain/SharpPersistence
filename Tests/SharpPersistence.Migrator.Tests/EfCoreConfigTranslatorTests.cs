using SharpPersistence.Migrator.Cli.Translators;
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

    [Fact]
    public void TranslateSource_TranslatesMetadataHasSchema_ToTableWithSchema_AndExcludeFromMigrations()
    {
        const string efSource = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Demo;

            public class RefProductConfiguration : IEntityTypeConfiguration<RefProduct>
            {
                public void Configure(EntityTypeBuilder<RefProduct> builder)
                {
                    builder.ToTable("products", "inventory", t =>
                    {
                        t.ExcludeFromMigrations();
                    });
                }
            }

            public class LegacyOrderConfiguration : IEntityTypeConfiguration<LegacyOrder>
            {
                public void Configure(EntityTypeBuilder<LegacyOrder> builder)
                {
                    builder.Metadata.HasSchema("sales");
                    builder.ToTable("legacy_orders");
                }
            }
            """;

        var result = EfCoreConfigTranslator.TranslateSource(efSource, "RefProductConfiguration.cs");

        Assert.NotNull(result);
        Assert.Contains("builder.ToTable(\"products\").HasSchema(\"inventory\");", result.TranslatedSource);
        Assert.Contains("builder.ExcludeFromMigrations();", result.TranslatedSource);
        Assert.Contains("builder.HasSchema(\"sales\");", result.TranslatedSource);
    }

    [Fact]
    public void TranslateSource_TranslatesEntityAndPropertyAnnotations()
    {
        const string efSource = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Demo;

            public class AuditOrderConfiguration : IEntityTypeConfiguration<Order>
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Metadata.SetAnnotation("Audit:TrackChanges", true);
                    builder.Property(x => x.Secret).Metadata.SetAnnotation("Sensitive:Mask", true);
                    builder.Property(x => x.Code).HasAnnotation("Custom:Tag", "v1");
                }
            }
            """;

        var result = EfCoreConfigTranslator.TranslateSource(efSource, "AuditOrderConfiguration.cs");

        Assert.NotNull(result);
        Assert.Contains("builder.SetAnnotation(\"Audit:TrackChanges\", true);", result.TranslatedSource);
        Assert.Contains("builder.Property(x => x.Secret).SetAnnotation(\"Sensitive:Mask\", true);", result.TranslatedSource);
        Assert.Contains("builder.Property(x => x.Code).SetAnnotation(\"Custom:Tag\", \"v1\");", result.TranslatedSource);
    }

    [Fact]
    public void TranslateDirectory_WithoutOutputPath_WritesToTranslatedEfConfigsAndLeavesOriginalsIntact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_cfg_trans_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempRoot, "Sales");
        Directory.CreateDirectory(subDir);

        const string originalSource = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Demo;

            public class OrderConfiguration : IEntityTypeConfiguration<Order>
            {
                public void Configure(EntityTypeBuilder<Order> builder) => builder.ToTable("orders");
            }
            """;

        try
        {
            var sourceFile = Path.Combine(subDir, "OrderConfiguration.cs");
            File.WriteAllText(sourceFile, originalSource);

            var firstRun = EfCoreConfigTranslator.TranslateDirectory(tempRoot);
            Assert.Single(firstRun);

            Assert.Equal(originalSource, File.ReadAllText(sourceFile));

            var translatedFile = Path.Combine(
                tempRoot,
                EfCoreConfigTranslator.DefaultOutputDirectoryName,
                "Sales",
                "OrderConfiguration.cs");
            Assert.True(File.Exists(translatedFile));
            Assert.Contains("IEntityConfiguration<Order>", File.ReadAllText(translatedFile));

            var secondRun = EfCoreConfigTranslator.TranslateDirectory(tempRoot);
            Assert.Single(secondRun);
            Assert.False(Directory.Exists(Path.Combine(
                tempRoot,
                EfCoreConfigTranslator.DefaultOutputDirectoryName,
                EfCoreConfigTranslator.DefaultOutputDirectoryName)));

            Assert.Throws<IOException>(() => EfCoreConfigTranslator.TranslateDirectory(sourceFile));
            Assert.Throws<IOException>(() => EfCoreConfigTranslator.TranslateDirectory(tempRoot, outputDirectory: sourceFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TranslateDirectory_RewritesNamespacesForDefaultOutput_SiblingOutput_AndExplicitNamespace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_cfg_ns_{Guid.NewGuid():N}");
        var entityConfigsDir = Path.Combine(tempRoot, "Persistence", "EntityConfigs");
        var billingDir = Path.Combine(entityConfigsDir, "Billing");
        Directory.CreateDirectory(billingDir);

        try
        {
            File.WriteAllText(
                Path.Combine(entityConfigsDir, "UserConfig.cs"),
                """
                using Microsoft.EntityFrameworkCore;
                using Microsoft.EntityFrameworkCore.Metadata.Builders;

                namespace AccessControlModule.Infrastructure.Persistence.EntityConfigs;

                public class UserConfig : IEntityTypeConfiguration<User>
                {
                    public void Configure(EntityTypeBuilder<User> builder) => builder.ToTable("users");
                }
                """);

            File.WriteAllText(
                Path.Combine(billingDir, "InvoiceConfig.cs"),
                """
                using Microsoft.EntityFrameworkCore;
                using Microsoft.EntityFrameworkCore.Metadata.Builders;

                namespace AccessControlModule.Infrastructure.Persistence.EntityConfigs.Billing
                {
                    public class InvoiceConfig : IEntityTypeConfiguration<Invoice>
                    {
                        public void Configure(EntityTypeBuilder<Invoice> builder) => builder.ToTable("invoices");
                    }
                }
                """);

            // 1. Default output (<source>/TranslatedEfConfigs)
            var defaultTranslated = EfCoreConfigTranslator.TranslateDirectory(entityConfigsDir, dryRun: true);
            var userDefault = defaultTranslated.Single(t => t.EntityTypeName == "User");
            var invDefault = defaultTranslated.Single(t => t.EntityTypeName == "Invoice");

            Assert.Contains(
                "namespace AccessControlModule.Infrastructure.Persistence.EntityConfigs.TranslatedEfConfigs;",
                userDefault.TranslatedSource);
            Assert.Contains(
                "namespace AccessControlModule.Infrastructure.Persistence.EntityConfigs.TranslatedEfConfigs.Billing",
                invDefault.TranslatedSource);

            // 2. Sibling --output-path (../MigratorConfigs)
            var siblingOutput = Path.Combine(tempRoot, "Persistence", "MigratorConfigs");
            var siblingTranslated = EfCoreConfigTranslator.TranslateDirectory(entityConfigsDir, siblingOutput, dryRun: true);
            var userSibling = siblingTranslated.Single(t => t.EntityTypeName == "User");
            var invSibling = siblingTranslated.Single(t => t.EntityTypeName == "Invoice");

            Assert.Contains(
                "namespace AccessControlModule.Infrastructure.Persistence.MigratorConfigs;",
                userSibling.TranslatedSource);
            Assert.Contains(
                "namespace AccessControlModule.Infrastructure.Persistence.MigratorConfigs.Billing",
                invSibling.TranslatedSource);

            // 3. Explicit --namespace override
            var explicitTranslated = EfCoreConfigTranslator.TranslateDirectory(
                entityConfigsDir,
                siblingOutput,
                dryRun: true,
                targetNamespace: "MyApp.Custom.Configs");
            var userExplicit = explicitTranslated.Single(t => t.EntityTypeName == "User");
            var invExplicit = explicitTranslated.Single(t => t.EntityTypeName == "Invoice");

            Assert.Contains("namespace MyApp.Custom.Configs;", userExplicit.TranslatedSource);
            Assert.Contains("namespace MyApp.Custom.Configs.Billing", invExplicit.TranslatedSource);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TranslateSource_RewritesHasOperators_StripsCustomMetadata_AndPreservesHasOneBlankLines()
    {
        const string input = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace Demo.Configs;

            public class UserGroupConfig : IEntityTypeConfiguration<UserGroup>
            {
                public void Configure(EntityTypeBuilder<UserGroup> builder)
                {
                    builder.HasKey(x => new { x.UserGroupHeadId, x.UserId });

                    builder.Property(x => x.NormalizedName).Metadata.SkipAutomatedIndexCreation();

                    builder.HasIndex(x => x.Code)
                        .HasOperators("gin_trgm_ops");

                    builder.HasIndex(x => new { x.NormalizedName, x.UserId })
                        .IsUnique()
                        .HasOperators(CommonInfrastructureConstants.VarcharPgOperator, string.Empty);

                    builder.HasIndex(x => new { x.FirstName, x.LastName })
                        .HasOperators("varchar_pattern_ops", "varchar_pattern_ops");

                    builder.HasOne<UserGroupHead>()
                        .WithMany()
                        .HasForeignKey(x => x.UserGroupHeadId)
                        .HasConstraintName("fk_user_group_head")
                        .OnDelete(DeleteBehavior.Restrict);

                    builder.HasOne<User>()
                        .WithMany()
                        .HasForeignKey(x => x.UserId)
                        .HasConstraintName("fk_user_group_user")
                        .OnDelete(DeleteBehavior.Restrict);
                }
            }
            """;

        var result = EfCoreConfigTranslator.TranslateSource(input);

        Assert.NotNull(result);
        Assert.Contains("using SharpPersistence.Migrator.PostgreSql;", result.TranslatedSource);
        Assert.Contains("builder.Property(x => x.NormalizedName).SkipAutomatedIndexCreation();", result.TranslatedSource);
        Assert.DoesNotContain(".Metadata.SkipAutomatedIndexCreation()", result.TranslatedSource);
        Assert.Single(result.CustomMetadataHints!, h => h.MethodName == "SkipAutomatedIndexCreation" && h.IsColumnTarget && h.ArgumentCount == 0);

        Assert.Contains(""".HasOperator("gin_trgm_ops");""", result.TranslatedSource);
        Assert.Contains(".HasOperator(k => k.NormalizedName, CommonInfrastructureConstants.VarcharPgOperator);", result.TranslatedSource);
        Assert.Contains(""".HasOperator(k => k.FirstName, "varchar_pattern_ops")""", result.TranslatedSource);
        Assert.Contains(""".HasOperator(k => k.LastName, "varchar_pattern_ops")""", result.TranslatedSource);
        Assert.DoesNotContain("HasOperators", result.TranslatedSource);

        // Blank line between consecutive rewritten HasForeignKey statements must be preserved
        var normalized = result.TranslatedSource.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(".OnDelete(DbReferentialAction.Restrict);\n\n        builder.HasForeignKey(x => x.UserId)", normalized);

        // Blank line between file-scoped namespace and [EntityConfiguration<T>], with class directly below attribute
        Assert.Contains("namespace Demo.Configs;\n\n[EntityConfiguration<UserGroup>]\npublic sealed class UserGroupConfig", normalized);
    }
}
