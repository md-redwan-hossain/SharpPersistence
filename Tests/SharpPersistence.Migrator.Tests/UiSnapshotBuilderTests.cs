using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Cli;
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
    public void UiDataService_BuildsLightweightOverview_LazyTableDetail_AndErd()
    {
        var services = new ServiceCollection();
        services.AddDbMigrator(opt => opt
            .UsePostgreSql("Host=localhost;Database=test")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .AddRegistry<UiBillingRegistry>());

        using var sp = services.BuildServiceProvider();
        var manager = (DbMigrator)sp.GetRequiredService<IDbMigrator>();
        var loaded = new LoadedMigrationContext(sp, manager, Directory.GetCurrentDirectory(), "TestHost", "TestHost.csproj", "Test", []);

        var dataService = new UiDataService(loaded);
        var overview = dataService.BuildOverview();

        Assert.Equal("TestHost", overview.ProjectName);
        Assert.Equal(1, overview.TotalEntities);
        var reg = Assert.Single(overview.Registries);
        Assert.Equal(nameof(UiBillingRegistry), reg.Name);

        var summary = Assert.Single(reg.Tables);
        Assert.Equal(nameof(UiInvoice), summary.ShortEntityName);
        Assert.Equal("billing.invoices", summary.QualifiedName);
        Assert.Equal(3, summary.ColumnCount);

        var detail = dataService.GetTableDetail(reg.Name, summary.EntityName);
        Assert.NotNull(detail);
        Assert.Equal("Customer invoices table", detail.Comment);

        var numCol = Assert.Single(detail.Columns, c => c.ColumnName == "invoice_number");
        Assert.Equal("character varying(50)", numCol.SqlType);
        Assert.Equal("Unique human-readable invoice code", numCol.Comment);
        Assert.False(numCol.IsNullable);

        var pkCol = Assert.Single(detail.Columns, c => c.ColumnName == "id");
        Assert.True(pkCol.IsPrimaryKey);

        var ix = Assert.Single(detail.Indexes);
        Assert.Equal("ux_invoices_number", ix.Name);
        Assert.True(ix.IsUnique);

        var ck = Assert.Single(detail.CheckConstraints);
        Assert.Equal("ck_invoices_total", ck.Name);

        var erd = dataService.BuildErd(reg.Name);
        var node = Assert.Single(erd.Nodes);
        Assert.Equal("UiInvoice", node.ShortEntityName);
        Assert.Empty(erd.Edges);
    }

    [Fact]
    public void LoadEmbeddedHtml_ReturnsValidHtmlTemplate()
    {
        var embeddedHtml = UiCommand.LoadEmbeddedHtml();
        Assert.Contains("<!doctype html>", embeddedHtml, StringComparison.OrdinalIgnoreCase);
    }
}
