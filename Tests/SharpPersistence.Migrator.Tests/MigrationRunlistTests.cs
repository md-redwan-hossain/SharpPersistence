using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class MigrationRunlistTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "SharpPersistence_RunlistTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class User
    {
        public long Id { get; set; }
    }

    private sealed class Order
    {
        public long Id { get; set; }
        public long UserId { get; set; }
    }

    private sealed class UsersRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<User>(b => b.ToTable("users"));
        }
    }

    private sealed class OrdersRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasForeignKey(x => x.UserId).References<User>(u => u.Id);
            });
        }
    }

    private DbMigrator CreateManager()
    {
        var builder = new DbMigratorOptionsBuilder();
        builder.UsePostgreSql("Host=localhost;Database=test")
            .UseRootPath(_tempDir)
            .AddRegistry<OrdersRegistry>()
            .AddRegistry<UsersRegistry>();
        return new DbMigrator(builder.Build(), new EmptyServiceProvider());
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public void StampHeader_IsIdempotent_AndUsesUuidV7()
    {
        var (stamped, id, alreadyHad) = MigrationScriptHeader.StampHeader("BEGIN;\nCOMMIT;\n");
        Assert.False(alreadyHad);
        Assert.NotEqual(Guid.Empty, id);
        Assert.StartsWith("-- migration-script-id: ", stamped, StringComparison.Ordinal);

        Assert.True(MigrationScriptHeader.TryParseId(stamped, out var parsed));
        Assert.Equal(id, parsed);

        var (second, secondId, alreadyHadSecond) = MigrationScriptHeader.StampHeader(stamped);
        Assert.True(alreadyHadSecond);
        Assert.Equal(id, secondId);
        Assert.Equal(stamped, second);
    }

    [Fact]
    public async Task GenerateAsync_PrefixesFilenamesWithUuidN_NormalizesSnakeCase_AndWritesHyphenlessRunlist()
    {
        var manager = CreateManager();
        var generated = await manager.GenerateAsync("Add Trading Type", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, generated.Count);
        Assert.All(generated, g =>
        {
            Assert.NotNull(g.ScriptPath);
            Assert.True(MigrationScriptHeader.TryParseIdFromFileName(g.ScriptPath!, out _));
            Assert.EndsWith("_add_trading_type.sql", Path.GetFileName(g.ScriptPath!), StringComparison.Ordinal);
            Assert.DoesNotContain("-- migration-script-id:", g.SqlScript, StringComparison.Ordinal);
        });

        var runlist = manager.CreateRunlistManager();
        Assert.True(File.Exists(runlist.RunlistPath));
        var runlistJson = await File.ReadAllTextAsync(runlist.RunlistPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("-", runlistJson, StringComparison.Ordinal);

        var effective = runlist.ResolveEffectiveOrder();
        Assert.Equal(2, effective.Count);
        Assert.Equal("UsersRegistry", effective[0].RegistryName);
        Assert.Equal("OrdersRegistry", effective[1].RegistryName);

        // Deleting db-migration-runlist.json must fail (no fallback ordering)
        File.Delete(runlist.RunlistPath);
        var ex = Assert.Throws<InvalidOperationException>(() => runlist.ResolveEffectiveOrder());
        Assert.Contains("db-migration-runlist.json", ex.Message);

        // doctor also reports missing runlist as an error
        var checks = runlist.Validate();
        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("db-migration-runlist.json"));

        // runlist sync regenerates it
        var syncResult = await runlist.SyncAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, syncResult.AddedCount);
        Assert.Equal(0, syncResult.StampedCount);
        Assert.True(File.Exists(runlist.RunlistPath));
    }

    [Fact]
    public async Task OverridesJson_ReordersSubsetOfScripts_SupportsComments_AndValidatesErrors()
    {
        var manager = CreateManager();
        await manager.GenerateAsync("Initial", ct: TestContext.Current.CancellationToken);

        // Add 3 empty scripts so we have 5 total scripts
        var empty1 = await manager.GenerateAsync("StepOne", allowEmpty: true, ct: TestContext.Current.CancellationToken);
        await Task.Delay(1100, TestContext.Current.CancellationToken); // ensure distinct timestamp
        var empty2 = await manager.GenerateAsync("StepTwo", allowEmpty: true, ct: TestContext.Current.CancellationToken);

        var runlist = manager.CreateRunlistManager();
        var baseOrder = runlist.ResolveEffectiveOrder();
        Assert.True(baseOrder.Count >= 4);

        var idA = baseOrder[2].Id;
        var idB = baseOrder[3].Id;

        // 1. Valid override with JSONC comments: place idB before idA
        await File.WriteAllTextAsync(
            runlist.OverridesPath,
            $$"""
            [
              // Swap StepTwo before StepOne
              [
                "{{idB:D}}",
                "{{idA:D}}", // trailing comma
              ],
            ]
            """,
            TestContext.Current.CancellationToken);

        var overridden = runlist.ResolveEffectiveOrder();
        var idxB = overridden.ToList().FindIndex(s => s.Id == idB);
        var idxA = overridden.ToList().FindIndex(s => s.Id == idA);
        Assert.True(idxB < idxA);
        Assert.True(overridden[idxB].IsOverridden);
        Assert.True(overridden[idxA].IsOverridden);
        Assert.False(overridden[0].IsOverridden);

        // 2. Less than 2 IDs in an override sequence must fail
        await File.WriteAllTextAsync(runlist.OverridesPath, $"[[\"{idA:D}\"]]", TestContext.Current.CancellationToken);
        var shortChecks = runlist.Validate();
        Assert.Contains(shortChecks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("at least 2"));

        // 3. Non-existent ID in override must fail
        await File.WriteAllTextAsync(
            runlist.OverridesPath,
            $"[[\"{idA:D}\", \"{Guid.NewGuid():D}\"]]",
            TestContext.Current.CancellationToken);
        var missingChecks = runlist.Validate();
        Assert.Contains(missingChecks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("non-existent"));

        // 4. Cyclic override must fail
        await File.WriteAllTextAsync(
            runlist.OverridesPath,
            $"[[\"{idA:D}\", \"{idB:D}\"], [\"{idB:D}\", \"{idA:D}\"]]",
            TestContext.Current.CancellationToken);
        var cycleChecks = runlist.Validate();
        Assert.Contains(cycleChecks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("cyclic", StringComparison.OrdinalIgnoreCase));

        // 5. Override putting Orders (with FK -> users) before Users must be flagged by SQL DDL dependency analyzer
        var usersId = baseOrder[0].Id;
        var ordersId = baseOrder[1].Id;
        await File.WriteAllTextAsync(
            runlist.OverridesPath,
            $"[[\"{ordersId:D}\", \"{usersId:D}\"]]",
            TestContext.Current.CancellationToken);
        var ddlChecks = runlist.Validate();
        Assert.Contains(ddlChecks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("before it is created"));
    }

    [Fact]
    public void ComputeSha256_IgnoresMigrationScriptIdHeader_AndNormalizesLineEndings()
    {
        const string legacySql = "BEGIN;\r\nCREATE TABLE users (id bigint);\r\nCOMMIT;\r\n";
        var (stampedSql, _, _) = MigrationScriptHeader.StampHeader(legacySql);
        var unixStampedSql = stampedSql.Replace("\r\n", "\n", StringComparison.Ordinal);

        var legacyHash = MigrationScriptStore.ComputeSha256(legacySql);
        var stampedHash = MigrationScriptStore.ComputeSha256(stampedSql);
        var unixStampedHash = MigrationScriptStore.ComputeSha256(unixStampedSql);

        Assert.Equal(legacyHash, stampedHash);
        Assert.Equal(legacyHash, unixStampedHash);
    }

    [Fact]
    public async Task Overrides_PreservesUntouchedBaseOrder_WhenMovingLaterItemEarlier()
    {
        var manager = CreateManager();
        var runlist = manager.CreateRunlistManager();
        var dir = Path.Combine(_tempDir, "MigrationScripts", "Custom");
        Directory.CreateDirectory(dir);

        var ids = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        var names = new[]
        {
            $"{ids[0]:N}_01_a.sql",
            $"{ids[1]:N}_02_b.sql",
            $"{ids[2]:N}_03_c.sql",
            $"{ids[3]:N}_04_d.sql",
            $"{ids[4]:N}_05_e.sql"
        };
        for (var i = 0; i < 5; i++)
        {
            // Include SQL comments mentioning REFERENCES to confirm comment stripping works too
            var content = $"-- REFERENCES non_existent_table\nSELECT {i + 1};\n";
            await File.WriteAllTextAsync(Path.Combine(dir, names[i]), content, TestContext.Current.CancellationToken);
        }

        await runlist.AppendNewScriptsAsync(ids, TestContext.Current.CancellationToken);

        // Move E before B: expected [A, E, B, C, D] (B -> C -> D must stay intact)
        await File.WriteAllTextAsync(
            runlist.OverridesPath,
            $"[[\"{ids[4]:N}\", \"{ids[1]:N}\"]]",
            TestContext.Current.CancellationToken);

        var effective = runlist.ResolveEffectiveOrder();
        Assert.Equal(
            [names[0], names[4], names[1], names[2], names[3]],
            effective.Select(e => e.ScriptName).ToArray());

        var checks = runlist.Validate();
        Assert.DoesNotContain(checks, c => c.Severity == DoctorSeverity.Error);
    }
}
