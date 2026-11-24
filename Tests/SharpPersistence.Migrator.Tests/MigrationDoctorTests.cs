using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.PostgreSql;
using SharpPersistence.Migrator.Services;
using Shouldly;

namespace SharpPersistence.Migrator.Tests;

public sealed class MigrationDoctorTests
{
    private sealed class DoctorUser
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
    }

    private sealed class DoctorOrder
    {
        public long Id { get; set; }
        public Guid UserId { get; set; }
    }

    private sealed class DoctorBrokenOrder
    {
        public long Id { get; set; }
        public long UserId { get; set; } // Mismatched type vs DoctorUser.Id (Guid)
    }

    private sealed class DoctorGhostRef
    {
        public long Id { get; set; }
        public long MissingId { get; set; }
    }

    private sealed class DoctorMissingPrincipal
    {
        public long Id { get; set; }
    }

    private sealed class DoctorCollisionEntity
    {
        public long Id { get; set; }
    }

    private sealed class HealthyCoreRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DoctorUser>(e => e.ToTable("doctor_users"));
            registry.Entity<DoctorOrder>(e =>
            {
                e.ToTable("doctor_orders");
                e.HasForeignKey(x => x.UserId)
                    .References<DoctorUser>(u => u.Id)
                    .HasConstraintName("fk_doctor_orders_user_id");
            });
        }
    }

    private sealed class BrokenFkRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DoctorUser>(e => e.ToTable("doctor_users"));
            registry.Entity<DoctorBrokenOrder>(e =>
            {
                e.ToTable("doctor_broken_orders");
                e.HasForeignKey(x => x.UserId)
                    .References<DoctorUser>(u => u.Id)
                    .HasConstraintName("fk_broken_user_id");
            });
            registry.Entity<DoctorGhostRef>(e =>
            {
                e.ToTable("doctor_ghost_refs");
                e.HasForeignKey(x => x.MissingId)
                    .References<DoctorMissingPrincipal>(m => m.Id)
                    .HasConstraintName("fk_missing_principal");
            });
        }
    }

    private sealed class CollidingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DoctorCollisionEntity>(e => e.ToTable("doctor_users"));
        }
    }

    private sealed class OracleExclusionRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DoctorOrder>(e =>
            {
                e.ToTable("doctor_orders");
                e.HasExclusionConstraint("excl_order_user")
                    .WithElement(x => x.UserId, PostgresExclusionOperator.Equals);
            });
        }
    }

    private sealed class ExplicitPublicSchemaCollidingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DoctorCollisionEntity>(e => e.ToTable("doctor_users", "public"));
        }
    }

    private sealed class SerialPrincipal
    {
        public int Id { get; set; }
        public TimeSpan Duration { get; set; }
    }

    private sealed class IntegerDependent
    {
        public int Id { get; set; }
        public int PrincipalId { get; set; }
    }

    private sealed class PostgresAliasFkRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<SerialPrincipal>(e =>
            {
                e.ToTable("serial_principals");
                e.Property(x => x.Id).HasColumnType("serial");
            });
            registry.Entity<IntegerDependent>(e =>
            {
                e.ToTable("integer_dependents");
                e.HasForeignKey(x => x.PrincipalId)
                    .References<SerialPrincipal>(p => p.Id)
                    .HasConstraintName("fk_integer_dependents_serial");
            });
        }
    }

    private sealed class DuplicateIndexRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DoctorUser>(e =>
            {
                e.ToTable("doctor_users");
                e.HasIndex(x => x.Email).HasDatabaseName("ix_shared_name");
            });
            registry.Entity<DoctorOrder>(e =>
            {
                e.ToTable("doctor_orders");
                e.HasIndex(x => x.UserId).HasDatabaseName("ix_shared_name");
            });
        }
    }

    private static MigrationManager BuildManager(Action<MigrationManagerOptionsBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddDbMigrator(configure);
        var provider = services.BuildServiceProvider();
        return (MigrationManager)provider.GetRequiredService<IMigrationManager>();
    }

    private static DbConnection ThrowOnCall(IServiceProvider _) =>
        throw new InvalidOperationException("Offline doctor should not open a DB connection.");

    [Fact]
    public void DefaultHistoryTableName_IsDbMigrationLogs()
    {
        var options = new MigrationManagerOptions();
        options.HistoryTableName.ShouldBe("__db_migration_logs");
    }

    [Fact]
    public async Task DiagnoseAsync_HealthyRegistry_PassesAndReportsCoverage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_doctor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var manager = BuildManager(opt => opt
                .UsePostgreSql(ThrowOnCall)
                .UseRootPath(tempRoot)
                .AddRegistry<HealthyCoreRegistry>());

            // Before generating any migration script -> warns about unmigrated entity changes
            var beforeReport = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);
            beforeReport.HasErrors.ShouldBeFalse();
            beforeReport.RegistryReports.Count.ShouldBe(1);
            beforeReport.RegistryReports[0].RegistryName.ShouldBe(nameof(HealthyCoreRegistry));
            beforeReport.RegistryReports[0].HasErrors.ShouldBeFalse();
            beforeReport.MigrationCoverageChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Warning &&
                c.Message.Contains("not in a migration script yet", StringComparison.Ordinal));

            // Generate migration script & snapshot -> all entity changes covered
            await manager.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: TestContext.Current.CancellationToken);

            var afterReport = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);
            afterReport.HasErrors.ShouldBeFalse();
            afterReport.MigrationCoverageChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Pass &&
                c.Message == "All entity changes are covered by migration scripts");
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
    public async Task DiagnoseAsync_CollectsMultipleFkErrorsInSinglePass()
    {
        var manager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<BrokenFkRegistry>());

        var report = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);

        report.HasErrors.ShouldBeTrue();
        var reg = report.RegistryReports.ShouldHaveSingleItem();
        reg.Items.ShouldContain(i =>
            i.Severity == DoctorSeverity.Error &&
            i.EntityName == nameof(DoctorBrokenOrder) &&
            i.Message.Contains("fk_broken_user_id", StringComparison.Ordinal));
        reg.Items.ShouldContain(i =>
            i.Severity == DoctorSeverity.Error &&
            i.EntityName == nameof(DoctorGhostRef) &&
            i.Message.Contains("fk_missing_principal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnoseAsync_DetectsCrossRegistryTableCollision()
    {
        var manager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<HealthyCoreRegistry>()
            .AddRegistry<CollidingRegistry>());

        var report = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);

        report.HasErrors.ShouldBeTrue();
        report.CrossRegistryChecks.ShouldContain(c =>
            c.Severity == DoctorSeverity.Error &&
            c.Message.Contains("Table ownership collision", StringComparison.Ordinal) &&
            c.Message.Contains("doctor_users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnoseAsync_DetectsOracleExclusionConstraintError()
    {
        var manager = BuildManager(opt => opt
            .UseOracle(ThrowOnCall)
            .AddRegistry<OracleExclusionRegistry>());

        var report = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);

        report.HasErrors.ShouldBeTrue();
        report.RegistryReports.ShouldHaveSingleItem().Items.ShouldContain(i =>
            i.Severity == DoctorSeverity.Error &&
            i.Message.Contains("only supported on PostgreSQL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnoseAsync_DetectsImplicitVsExplicitPublicSchemaCollisionOnPostgres()
    {
        var manager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<HealthyCoreRegistry>()
            .AddRegistry<ExplicitPublicSchemaCollidingRegistry>());

        var report = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);

        report.HasErrors.ShouldBeTrue();
        report.CrossRegistryChecks.ShouldContain(c =>
            c.Severity == DoctorSeverity.Error &&
            c.Message.Contains("public.doctor_users", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiagnoseAsync_AllowsCompatiblePostgresTypeAliasesAndScansTimeSpan()
    {
        var manager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<PostgresAliasFkRegistry>());

        var report = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);

        report.HasErrors.ShouldBeFalse();
        // SerialPrincipal has Id + Duration (TimeSpan), IntegerDependent has Id + PrincipalId => 4 columns
        report.RegistryReports.ShouldHaveSingleItem().ColumnCount.ShouldBe(4);
    }

    [Fact]
    public async Task DiagnoseAsync_DetectsDuplicateIndexNameInSameSchema()
    {
        var manager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<DuplicateIndexRegistry>());

        var report = await manager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);

        report.HasErrors.ShouldBeTrue();
        report.CrossRegistryChecks.ShouldContain(c =>
            c.Severity == DoctorSeverity.Error &&
            c.Message.Contains("Duplicate index name 'ix_shared_name'", StringComparison.Ordinal));
    }

    private sealed class RefDoctorUser
    {
        public Guid Id { get; set; }
    }

    private sealed class MismatchedRefDoctorUser
    {
        public long Id { get; set; } // long instead of Guid
    }

    private sealed class ValidExcludedRefRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<RefDoctorUser>(e => e.ToTable("doctor_users").ExcludeFromMigrations());
        }
    }

    private sealed class MismatchedExcludedRefRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<MismatchedRefDoctorUser>(e => e.ToTable("doctor_users").ExcludeFromMigrations());
        }
    }

    [Fact]
    public async Task DiagnoseAsync_AllowsExcludedRefStubAndValidatesMatchingColumnTypes()
    {
        var validManager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<HealthyCoreRegistry>()
            .AddRegistry<ValidExcludedRefRegistry>());

        var validReport = await validManager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);
        validReport.HasErrors.ShouldBeFalse();
        validReport.RegistryReports.Single(r => r.RegistryName == nameof(HealthyCoreRegistry)).ExcludedEntityCount.ShouldBe(0);
        validReport.RegistryReports.Single(r => r.RegistryName == nameof(ValidExcludedRefRegistry)).ExcludedEntityCount.ShouldBe(1);

        var badManager = BuildManager(opt => opt
            .UsePostgreSql(ThrowOnCall)
            .AddRegistry<HealthyCoreRegistry>()
            .AddRegistry<MismatchedExcludedRefRegistry>());

        var badReport = await badManager.DiagnoseAsync(checkDatabase: false, ct: TestContext.Current.CancellationToken);
        badReport.HasErrors.ShouldBeTrue();
        badReport.CrossRegistryChecks.ShouldContain(c =>
            c.Severity == DoctorSeverity.Error &&
            c.Message.Contains("External ref mismatch", StringComparison.Ordinal));
    }
}
