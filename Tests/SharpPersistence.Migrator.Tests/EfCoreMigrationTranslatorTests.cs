using Microsoft.EntityFrameworkCore.Migrations;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.Enums;
using EfCoreMigrationTranslator = SharpPersistence.Migrator.Cli.Translators.EfCoreMigrationTranslator;

namespace SharpPersistence.Migrator.Tests;

[Migration("20260101000000_SampleInitial")]
public sealed class SampleEfCoreMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                email = table.Column<string>(maxLength: 256, nullable: false),
                role = table.Column<string>(maxLength: 32, nullable: false),
                is_active = table.Column<bool>(nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", x => x.id);
                table.CheckConstraint("ck_users_role", "role IN ('admin', 'user')");
            });

        migrationBuilder.InsertData(
            schema: "public",
            table: "users",
            columns: ["id", "email", "role", "is_active"],
            values: new object[] { Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin@example.com", "admin", true });

        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_custom ON public.users (email)");
    }
}

public sealed class EfCoreMigrationTranslatorTests
{
    [Fact]
    public void TranslateFromDirectory_Postgres_GeneratesFormattedMultiLineInsertAndDdl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "20260101000000_SampleInitial.cs"),
                "[Migration(\"20260101000000_SampleInitial\")]");

            var results = EfCoreMigrationTranslator.TranslateFromDirectory(
                tempDir,
                SqlDialect.PostgreSql,
                searchAssemblies: [typeof(SampleEfCoreMigration).Assembly]);

            var item = Assert.Single(results);
            Assert.Equal("20260101000000_SampleInitial", item.MigrationId);
            Assert.Contains("BEGIN;", item.SqlScript);
            Assert.Contains("CREATE TABLE IF NOT EXISTS \"public\".\"users\"", item.SqlScript);
            Assert.Contains("ck_users_role", item.SqlScript);
            Assert.Contains("INSERT INTO \"public\".\"users\" (", item.SqlScript);
            Assert.Contains("VALUES (", item.SqlScript);
            Assert.Contains("CREATE INDEX IF NOT EXISTS ix_custom ON public.users (email);", item.SqlScript);
            Assert.Contains("COMMIT;", item.SqlScript);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TranslateFromDirectory_Oracle19_EmitsCatalogGuardedDdlWithoutBeginCommit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "20260101000000_SampleInitial.cs"),
                "[Migration(\"20260101000000_SampleInitial\")]");

            var results = EfCoreMigrationTranslator.TranslateFromDirectory(
                tempDir,
                SqlDialect.Oracle,
                OracleDbCompatibility.Oracle19,
                searchAssemblies: [typeof(SampleEfCoreMigration).Assembly]);

            var item = Assert.Single(results);
            Assert.DoesNotContain("BEGIN;\n", item.SqlScript);
            Assert.Contains("ALL_TABLES WHERE OWNER = 'public' AND TABLE_NAME = 'users'", item.SqlScript);
            Assert.Contains("ALL_CONSTRAINTS WHERE OWNER = 'public' AND TABLE_NAME = 'users' AND CONSTRAINT_NAME = 'ck_users_role'", item.SqlScript);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
