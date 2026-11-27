using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.PostgreSql;
using SharpPersistence.Migrator.Services;
using Shouldly;
using Testcontainers.PostgreSql;

namespace SharpPersistence.Migrator.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "PostgresIntegration";
}

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("migrator_test_db")
        .WithUsername("migrator_test_user")
        .WithPassword("migrator_test_user")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    public async Task<string> CreateIsolatedDatabaseAsync(CancellationToken ct)
    {
        var dbName = $"sp_mig_{Guid.NewGuid():N}";
        await using var adminConn = new NpgsqlConnection(ConnectionString);
        await adminConn.OpenAsync(ct);

        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\";";
        await cmd.ExecuteNonQueryAsync(ct);

        var csb = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = dbName
        };
        return csb.ConnectionString;
    }
}

[Collection(PostgresIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresIntegrationTests(PostgresContainerFixture fixture)
{
    private enum BookingStatus
    {
        Pending,
        Confirmed,
        Cancelled
    }

    private sealed class PgCustomer
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    private sealed class PgBooking
    {
        public long Id { get; set; }
        public Guid CustomerId { get; set; }
        public int RoomId { get; set; }
        public decimal Amount { get; set; }
        public BookingStatus Status { get; set; }
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
    }

    private sealed class PgBookingRegistryV1 : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<PgCustomer>(e =>
            {
                e.ToTable("pg_customers");
                e.Property(x => x.FullName).HasMaxLength(150);
                e.Ignore(x => x.Email);
            });

            registry.Entity<PgBooking>(e =>
            {
                e.ToTable("pg_bookings");
                e.HasForeignKey(x => x.CustomerId)
                    .References<PgCustomer>(c => c.Id)
                    .HasConstraintName("fk_pg_bookings_customer_id")
                    .OnDelete(DbReferentialAction.Restrict);
                e.HasCheckConstraint("ck_pg_bookings_amount", cc => cc.Column(x => x.Amount).GreaterThan(0));
                e.HasExclusionConstraint("excl_pg_bookings_room_slot")
                    .WithElement(x => x.RoomId, PostgresExclusionOperator.Equals)
                    .WithExpression(
                        expr => expr.Raw("tstzrange(\"starts_at\", \"ends_at\", '[)')"),
                        PostgresExclusionOperator.Overlaps);
                e.HasIndex(x => x.CustomerId).HasDatabaseName("ix_pg_bookings_customer_id");
            });
        }
    }

    private sealed class PgBookingRegistryV2 : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<PgCustomer>(e =>
            {
                e.ToTable("pg_customers");
                e.Property(x => x.FullName).HasMaxLength(200);
                e.Property(x => x.Email).HasMaxLength(255);
                e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("uq_pg_customers_email");
            });

            registry.Entity<PgBooking>(e =>
            {
                e.ToTable("pg_bookings");
                e.HasForeignKey(x => x.CustomerId)
                    .References<PgCustomer>(c => c.Id)
                    .HasConstraintName("fk_pg_bookings_customer_id")
                    .OnDelete(DbReferentialAction.Restrict);
                e.HasCheckConstraint("ck_pg_bookings_amount", cc => cc.Column(x => x.Amount).GreaterThan(0));
                e.HasExclusionConstraint("excl_pg_bookings_room_slot")
                    .WithElement(x => x.RoomId, PostgresExclusionOperator.Equals)
                    .WithExpression(
                        expr => expr.Raw("tstzrange(\"starts_at\", \"ends_at\", '[)')"),
                        PostgresExclusionOperator.Overlaps);
                e.HasIndex(x => x.CustomerId).HasDatabaseName("ix_pg_bookings_customer_id");
            });
        }
    }

    private static DbMigrator BuildManager(
        string connectionString,
        string tempRoot,
        IEntityRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddDbMigrator(opt =>
        {
            opt.UsePostgreSql(connectionString)
                .HasPostgresExtension("btree_gist")
                .HasPostgresEnum<BookingStatus>("booking_status");
            opt.UseRootPath(tempRoot)
                .AddRegistry(registry);
        });

        var provider = services.BuildServiceProvider();
        return (DbMigrator)provider.GetRequiredService<IDbMigrator>();
    }

    [Fact]
    public async Task GenerateAndMigrate_CreatesExtensionsEnumsTablesConstraintsAndEnforcesRulesInPostgres()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbConnectionString = await fixture.CreateIsolatedDatabaseAsync(ct);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_pg_int_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var manager = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV1());

            var generated = await manager.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: ct);
            generated.Count.ShouldBe(2); // _global (btree_gist + enum) + PgBookingRegistryV1

            var applied = await manager.MigrateAsync(cancellationToken: ct);
            applied.Count.ShouldBe(2);

            (await manager.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
            var report = await manager.DiagnoseAsync(checkDatabase: true, ct: ct);
            report.HasErrors.ShouldBeFalse();

            // Verify live PostgreSQL enforces check constraint & GiST exclusion constraint
            await using var conn = new NpgsqlConnection(dbConnectionString);
            await conn.OpenAsync(ct);

            var customerId = Guid.NewGuid();
            await using (var insertCustomer = conn.CreateCommand())
            {
                insertCustomer.CommandText = "INSERT INTO \"pg_customers\" (\"id\", \"full_name\") VALUES (@id, 'Alice');";
                insertCustomer.Parameters.AddWithValue("@id", customerId);
                await insertCustomer.ExecuteNonQueryAsync(ct);
            }

            // Valid booking succeeds
            await using (var validBooking = conn.CreateCommand())
            {
                validBooking.CommandText = """
                    INSERT INTO "pg_bookings" ("id", "customer_id", "room_id", "amount", "status", "starts_at", "ends_at")
                    VALUES (1, @cid, 101, 250.00, 'confirmed'::"booking_status", '2026-12-01T10:00:00Z', '2026-12-01T12:00:00Z');
                    """;
                validBooking.Parameters.AddWithValue("@cid", customerId);
                await validBooking.ExecuteNonQueryAsync(ct);
            }

            // Check constraint rejects amount <= 0 (SQLSTATE 23514)
            var checkEx = await Should.ThrowAsync<PostgresException>(async () =>
            {
                await using var badAmount = conn.CreateCommand();
                badAmount.CommandText = """
                    INSERT INTO "pg_bookings" ("id", "customer_id", "room_id", "amount", "status", "starts_at", "ends_at")
                    VALUES (2, @cid, 102, 0.00, 'pending'::"booking_status", '2026-12-01T10:00:00Z', '2026-12-01T12:00:00Z');
                    """;
                badAmount.Parameters.AddWithValue("@cid", customerId);
                await badAmount.ExecuteNonQueryAsync(ct);
            });
            checkEx.SqlState.ShouldBe("23514");

            // Exclusion constraint rejects overlapping booking in same room (SQLSTATE 23P01)
            var exclEx = await Should.ThrowAsync<PostgresException>(async () =>
            {
                await using var overlapping = conn.CreateCommand();
                overlapping.CommandText = """
                    INSERT INTO "pg_bookings" ("id", "customer_id", "room_id", "amount", "status", "starts_at", "ends_at")
                    VALUES (3, @cid, 101, 150.00, 'pending'::"booking_status", '2026-12-01T11:00:00Z', '2026-12-01T13:00:00Z');
                    """;
                overlapping.Parameters.AddWithValue("@cid", customerId);
                await overlapping.ExecuteNonQueryAsync(ct);
            });
            exclEx.SqlState.ShouldBe("23P01");
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
    public async Task IncrementalMigration_AppliesV1ThenV2AndMatchesLiveSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbConnectionString = await fixture.CreateIsolatedDatabaseAsync(ct);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_pg_inc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var managerV1 = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV1());
            await managerV1.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: ct);
            await managerV1.MigrateAsync(cancellationToken: ct);

            // Copy V1 snapshot under V2's registry name to simulate evolving the same registry
            var stateDir = Path.Combine(tempRoot, DbMigratorOptions.DefaultSnapshotDirectory);
            File.Copy(
                Path.Combine(stateDir, $"{nameof(PgBookingRegistryV1)}.json"),
                Path.Combine(stateDir, $"{nameof(PgBookingRegistryV2)}.json"));

            var managerV2 = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV2());
            var v2Generated = await managerV2.GenerateAsync("AddCustomerEmail", interactiveRenamePrompt: false, ct: ct);
            v2Generated.ShouldContain(g => g.RegistryName == nameof(PgBookingRegistryV2));

            var v2Applied = await managerV2.MigrateAsync(cancellationToken: ct);
            v2Applied.ShouldContain(s => s.Contains("add_customer_email", StringComparison.Ordinal));

            var v2Report = await managerV2.DiagnoseAsync(checkDatabase: true, ct: ct);
            v2Report.HasErrors.ShouldBeFalse();
            var statuses = await managerV2.GetStatusAsync(ct);
            statuses.ShouldAllBe(s => s.IsApplied && !s.HasChecksumMismatch);
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
    public async Task DoctorCheckDb_IsReadOnlyBeforeMigrate_PassesAfterMigrate_AndDetectsLiveDiffsAndTamperedScripts()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbConnectionString = await fixture.CreateIsolatedDatabaseAsync(ct);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_pg_doc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var manager = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV1());
            var generated = await manager.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: ct);
            var mainScriptPath = generated.First(g => g.RegistryName == nameof(PgBookingRegistryV1)).ScriptPath!;

            // 1. Before MigrateAsync: doctor --check-db must NOT create __db_migrator_logs
            var beforeReport = await manager.DiagnoseAsync(checkDatabase: true, ct: ct);
            beforeReport.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Warning &&
                c.Message.Contains("__db_migrator_logs", StringComparison.Ordinal) &&
                c.Message.Contains("does not exist yet", StringComparison.Ordinal));
            beforeReport.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Error &&
                c.Message.Contains("Live database does not match your migration scripts", StringComparison.Ordinal));

            // Confirm __db_migrator_logs was genuinely not created (read-only guarantee)
            await using (var verifyConn = new NpgsqlConnection(dbConnectionString))
            {
                await verifyConn.OpenAsync(ct);
                await using var checkCmd = verifyConn.CreateCommand();
                checkCmd.CommandText = "SELECT to_regclass('\"public\".\"__db_migrator_logs\"') IS NOT NULL;";
                (await checkCmd.ExecuteScalarAsync(ct)).ShouldBe(false);
            }

            // 2. Apply migrations -> doctor --check-db passes cleanly
            await manager.MigrateAsync(cancellationToken: ct);
            var healthyReport = await manager.DiagnoseAsync(checkDatabase: true, ct: ct);
            healthyReport.HasErrors.ShouldBeFalse();
            healthyReport.MigrationCoverageChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Pass &&
                c.Message == "All entity changes are covered by migration scripts");
            healthyReport.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Pass &&
                c.Message == "Live database matches your migration scripts");

            // 3. Manually alter live DB + tamper with an applied script -> doctor catches BOTH
            await using (var tamperConn = new NpgsqlConnection(dbConnectionString))
            {
                await tamperConn.OpenAsync(ct);
                await using var alterCmd = tamperConn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE \"pg_customers\" ADD COLUMN \"manual_extra\" text;";
                await alterCmd.ExecuteNonQueryAsync(ct);
            }

            await File.AppendAllTextAsync(mainScriptPath, $"{Environment.NewLine}-- tampered{Environment.NewLine}", ct);

            var driftedReport = await manager.DiagnoseAsync(checkDatabase: true, ct: ct);
            driftedReport.HasErrors.ShouldBeTrue();
            driftedReport.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Error &&
                c.Message.Contains("modified script(s) after being applied", StringComparison.Ordinal));
            driftedReport.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Error &&
                c.Message.Contains("manual_extra", StringComparison.Ordinal));
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
    public async Task ConcurrentMigrateAsync_AdvisoryLockSerializesExecutionWithoutErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbConnectionString = await fixture.CreateIsolatedDatabaseAsync(ct);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_pg_lock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var managerA = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV1());
            var managerB = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV1());

            await managerA.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: ct);

            var results = await Task.WhenAll(
                managerA.MigrateAsync(cancellationToken: ct),
                managerB.MigrateAsync(cancellationToken: ct));

            var totalApplied = results[0].Count + results[1].Count;
            totalApplied.ShouldBe(2);
            (await managerA.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
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
    public async Task CanConnectHasPendingAndDesignTimeState_WorkAsExpected()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbConnectionString = await fixture.CreateIsolatedDatabaseAsync(ct);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_pg_api_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            DbMigratorState.IsDesignTime.ShouldBeFalse();
            AppContext.SetData(DbMigratorState.DesignTimeDataKey, true);
            try
            {
                DbMigratorState.IsDesignTime.ShouldBeTrue();
            }
            finally
            {
                AppContext.SetData(DbMigratorState.DesignTimeDataKey, null);
            }

            var manager = BuildManager(dbConnectionString, tempRoot, new PgBookingRegistryV1());
            (await manager.CanConnectToDatabaseAsync(ct)).ShouldBeTrue();

            var badManager = BuildManager("Host=127.0.0.1;Port=1;Database=nope;Timeout=1", tempRoot, new PgBookingRegistryV1());
            (await badManager.CanConnectToDatabaseAsync(ct)).ShouldBeFalse();

            using var canceledCts = new CancellationTokenSource();
            await canceledCts.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => manager.CanConnectToDatabaseAsync(canceledCts.Token));

            (await manager.HasPendingMigrationAsync(ct)).ShouldBeFalse();
            (await manager.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();

            await manager.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: ct);

            (await manager.HasPendingMigrationAsync(ct)).ShouldBeTrue();
            (await manager.GetPendingMigrationsAsync(ct)).Count.ShouldBe(2);

            await manager.MigrateAsync(cancellationToken: ct);

            (await manager.HasPendingMigrationAsync(ct)).ShouldBeFalse();
            (await manager.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
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
    public async Task PermissionPreflight_BlocksRestrictedRoleBeforeDdl_AndDoctorDetectsRevokedGrantAfterApply()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminConnStr = await fixture.CreateIsolatedDatabaseAsync(ct);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_pg_perms_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var roleName = $"limited_{Guid.NewGuid():N}"[..16];
        var rolePassword = "TestRolePassword123!";
        var adminCsb = new NpgsqlConnectionStringBuilder(adminConnStr);
        var restrictedCsb = new NpgsqlConnectionStringBuilder(adminConnStr)
        {
            Username = roleName,
            Password = rolePassword
        };

        try
        {
            await using (var adminConn = new NpgsqlConnection(adminConnStr))
            {
                await adminConn.OpenAsync(ct);
                await using var cmd = adminConn.CreateCommand();
                cmd.CommandText = $"""
                    CREATE ROLE "{roleName}" LOGIN PASSWORD '{rolePassword}';
                    REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                    GRANT CONNECT ON DATABASE "{adminCsb.Database}" TO "{roleName}";
                    GRANT USAGE ON SCHEMA public TO "{roleName}";
                    """;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var restrictedManager = BuildManager(restrictedCsb.ConnectionString, tempRoot, new PgBookingRegistryV1());
            await restrictedManager.GenerateAsync("Initial", interactiveRenamePrompt: false, ct: ct);

            // 1. MigrateAsync fails fast before creating __db_migrator_logs or running any DDL
            var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
                restrictedManager.MigrateAsync(cancellationToken: ct));
            ex.Message.ShouldContain("missing required permissions");
            ex.Message.ShouldContain("CREATE privilege on schema 'public'");

            await using (var verifyConn = new NpgsqlConnection(adminConnStr))
            {
                await verifyConn.OpenAsync(ct);
                await using var checkCmd = verifyConn.CreateCommand();
                checkCmd.CommandText = "SELECT to_regclass('\"public\".\"__db_migrator_logs\"') IS NOT NULL;";
                (await checkCmd.ExecuteScalarAsync(ct)).ShouldBe(false);
            }

            // 2. doctor --check-db reports the exact missing privilege + GRANT hint
            var beforeDoctor = await restrictedManager.DiagnoseAsync(checkDatabase: true, ct: ct);
            beforeDoctor.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Error &&
                c.Message.Contains("CREATE privilege on schema 'public'", StringComparison.Ordinal) &&
                c.Hint != null &&
                c.Hint.Contains("GRANT CREATE ON SCHEMA \"public\"", StringComparison.Ordinal));

            // 3. Grant CREATE on public + database -> MigrateAsync succeeds
            await using (var adminConn = new NpgsqlConnection(adminConnStr))
            {
                await adminConn.OpenAsync(ct);
                await using var cmd = adminConn.CreateCommand();
                cmd.CommandText = $"""
                    GRANT CREATE ON DATABASE "{adminCsb.Database}" TO "{roleName}";
                    GRANT CREATE ON SCHEMA public TO "{roleName}";
                    """;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var applied = await restrictedManager.MigrateAsync(cancellationToken: ct);
            applied.Count.ShouldBe(2);

            // 4. Revoke CREATE on public after migrations are applied -> doctor --check-db still catches it
            await using (var adminConn = new NpgsqlConnection(adminConnStr))
            {
                await adminConn.OpenAsync(ct);
                await using var cmd = adminConn.CreateCommand();
                cmd.CommandText = $"REVOKE CREATE ON SCHEMA public FROM \"{roleName}\";";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var afterRevokeDoctor = await restrictedManager.DiagnoseAsync(checkDatabase: true, ct: ct);
            afterRevokeDoctor.DatabaseChecks.ShouldContain(c =>
                c.Severity == DoctorSeverity.Error &&
                c.Message.Contains("CREATE privilege on schema 'public'", StringComparison.Ordinal));
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
