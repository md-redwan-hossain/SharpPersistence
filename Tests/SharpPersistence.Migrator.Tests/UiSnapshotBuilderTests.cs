using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Cli.Ui;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class UiSnapshotBuilderTests
{
    private sealed class UiInvoice
    {
        public long Id { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
    }

    private sealed class UiBillingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.HasDefaultSchema("billing");
            registry.Entity<UiInvoice>(b =>
            {
                b.ToTable("invoices")
                    .HasComment("Customer invoices table");
                b.Property(x => x.InvoiceNumber)
                    .HasMaxLength(50)
                    .HasComment("Unique human-readable invoice code");
                b.Property(x => x.TotalAmount)
                    .HasPrecision(18, 4);
                b.HasIndex(x => x.InvoiceNumber)
                    .IsUnique()
                    .HasDatabaseName("ux_invoices_number");
                b.HasCheckConstraint("ck_invoices_total", expr => expr.Column(x => x.TotalAmount).GreaterThanOrEqual(0m));
            });
        }
    }

    [Fact]
    public void BuildModel_ExportsFullDbMetadataAndDdlPreview()
    {
        var services = new ServiceCollection();
        services.AddDbMigrator(opt => opt
            .UsePostgreSql("Host=localhost;Database=test")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .AddRegistry<UiBillingRegistry>());

        using var sp = services.BuildServiceProvider();
        var manager = (MigrationManager)sp.GetRequiredService<IMigrationManager>();

        var model = UiSnapshotBuilder.BuildModel(manager);

        var reg = Assert.Single(model.Registries);
        Assert.Equal(nameof(UiBillingRegistry), reg.Name);

        var table = Assert.Single(reg.Tables);
        Assert.Equal(nameof(UiInvoice), table.EntityName);
        Assert.Equal("billing", table.Schema);
        Assert.Equal("invoices", table.TableName);
        Assert.Equal("billing.invoices", table.QualifiedName);
        Assert.Equal("Customer invoices table", table.Comment);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"billing\".\"invoices\"", table.DdlPreview);

        var numCol = Assert.Single(table.Columns, c => c.ColumnName == "invoice_number");
        Assert.Equal("character varying(50)", numCol.SqlType);
        Assert.Equal("Unique human-readable invoice code", numCol.Comment);
        Assert.False(numCol.IsNullable);

        var pkCol = Assert.Single(table.Columns, c => c.ColumnName == "id");
        Assert.True(pkCol.IsPrimaryKey);

        var ix = Assert.Single(table.Indexes);
        Assert.Equal("ux_invoices_number", ix.Name);
        Assert.True(ix.IsUnique);

        var ck = Assert.Single(table.CheckConstraints);
        Assert.Equal("ck_invoices_total", ck.Name);
    }

    [Fact]
    public void InjectSnapshotIntoHtml_EscapesClosingScriptTag_AndLoadsEmbeddedResource()
    {
        var embeddedHtml = UiCommand.LoadEmbeddedHtml();
        Assert.Contains(UiSnapshotBuilder.SnapshotPlaceholder, embeddedHtml);

        var snapshot = new UiSnapshotDto(
            ProjectName: "Demo</script><script>alert(1)</script>",
            StartupProjectPath: "Demo.csproj",
            Dialect: "PostgreSql",
            GeneratedAtUtc: "2026-11-25T00:00:00Z",
            Model: new UiModelDto([], [], []),
            Runlist: new UiRunlistDto(null, []),
            Doctor: new UiDoctorDto(false, 1, 0, 0, [], [], [], [], []));

        var exported = UiSnapshotBuilder.InjectSnapshotIntoHtml(embeddedHtml, snapshot);

        Assert.DoesNotContain(UiSnapshotBuilder.SnapshotPlaceholder, exported);
        Assert.DoesNotContain("Demo</script>", exported);
        Assert.Contains(@"Demo<\/script>", exported);
    }
}
