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

    private sealed class UiUser
    {
        public long Id { get; set; }
    }

    private sealed class UiRefUser
    {
        public long Id { get; set; }
    }

    private sealed class UiAuditInvoice
    {
        public long Id { get; set; }
        public long CreatedByUserId { get; set; }
    }

    private sealed class UiIdentityRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.HasDefaultSchema("identity");
            registry.Entity<UiUser>(b => b.ToTable("users"));
        }
    }

    private sealed class UiDependentBillingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.HasDefaultSchema("billing");
            registry.Entity<UiAuditInvoice>(b =>
            {
                b.ToTable("audit_invoices");
                b.HasForeignKey(x => x.CreatedByUserId)
                    .References<UiRefUser>(u => u.Id)
                    .HasConstraintName("fk_audit_invoices_user");
            });
        }
    }

    [Fact]
    public void UiDataService_BuildDependencies_BuildDryRun_AndSearchSchema_WorkEndToEnd()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_ui_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var services = new ServiceCollection();
            services.AddDbMigrator(opt => opt
                .UsePostgreSql("Host=localhost;Database=test")
                .UseRootPath(tempRoot)
                .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
                .ReplaceEntityTypes(r => r.Replace<UiRefUser, UiUser>())
                .AddRegistry<UiDependentBillingRegistry>()
                .AddRegistry<UiIdentityRegistry>()
                .AddRegistry<UiBillingRegistry>());

            using var sp = services.BuildServiceProvider();
            var manager = (DbMigrator)sp.GetRequiredService<IDbMigrator>();
            var loaded = new LoadedMigrationContext(sp, manager, tempRoot, "TestHost", "TestHost.csproj", "Test", []);
            var dataService = new UiDataService(loaded);

            // 1. Dependencies graph & stub map
            var deps = dataService.BuildDependencies();
            Assert.Equal(3, deps.Nodes.Count);
            var idOrder = deps.Nodes.Single(n => n.RegistryName == nameof(UiIdentityRegistry)).Order;
            var depOrder = deps.Nodes.Single(n => n.RegistryName == nameof(UiDependentBillingRegistry)).Order;
            Assert.True(idOrder < depOrder);

            var edge = Assert.Single(deps.Edges);
            Assert.Equal(nameof(UiDependentBillingRegistry), edge.FromRegistry);
            Assert.Equal(nameof(UiIdentityRegistry), edge.ToRegistry);
            var fk = Assert.Single(edge.ForeignKeys);
            Assert.Equal("fk_audit_invoices_user", fk.ConstraintName);

            var stub = Assert.Single(deps.Stubs);
            Assert.Equal(nameof(UiRefUser), stub.ShortStubEntity);
            Assert.Equal(nameof(UiUser), stub.ShortTargetEntity);
            Assert.Equal(nameof(UiIdentityRegistry), stub.TargetRegistry);
            Assert.Equal("identity.users", stub.TargetTable);

            // 2. Dry Run (no snapshots on disk yet -> all registries have pending additions)
            var dryRun = dataService.BuildDryRun();
            Assert.Equal(3, dryRun.ChangedRegistryCount);
            Assert.True(dryRun.AddCount > 0);
            Assert.Equal(0, dryRun.DropCount);
            var billingDry = dryRun.Registries.Single(r => r.RegistryName == nameof(UiBillingRegistry));
            Assert.True(billingDry.HasChanges);
            Assert.Contains("CREATE TABLE", billingDry.SqlScript);

            // 3. Schema search across columns, indexes, and constraints
            var colHits = dataService.SearchSchema("invoice_number");
            Assert.Contains(colHits.Hits, h => h.Kind == "Column" && h.Name == "invoice_number");

            var ixHits = dataService.SearchSchema("ux_invoices_number");
            Assert.Contains(ixHits.Hits, h => h.Kind == "Index" && h.Name == "ux_invoices_number");

            var ckHits = dataService.SearchSchema("ck_invoices_total");
            Assert.Contains(ckHits.Hits, h => h.Kind == "CheckConstraint" && h.Name == "ck_invoices_total");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
