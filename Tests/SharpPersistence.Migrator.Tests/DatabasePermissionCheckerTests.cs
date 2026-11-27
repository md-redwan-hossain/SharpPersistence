using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class DatabasePermissionCheckerTests
{
    [Fact]
    public void Extractor_ParsesPostgresDdlAndHandWrittenSql_WithDeduplication()
    {
        var options = new DbMigratorOptions
        {
            Dialect = SqlDialect.PostgreSql,
            HistorySchema = "public",
            HistoryTableName = "__db_migrator_logs"
        };
        var dialect = MigrationDialectRegistry.Get(options);

        var script1 = """
            -- Initial migration
            CREATE EXTENSION IF NOT EXISTS "pg_trgm";
            CREATE SCHEMA IF NOT EXISTS "billing";
            CREATE TYPE "billing"."invoice_status" AS ENUM ('draft', 'paid');
            CREATE SEQUENCE IF NOT EXISTS "billing"."invoice_seq";
            CREATE TABLE IF NOT EXISTS "billing"."invoices" (
                "id" bigint NOT NULL,
                "user_id" bigint NOT NULL,
                CONSTRAINT "fk_invoices_users" FOREIGN KEY ("user_id") REFERENCES "access_control"."users" ("id")
            ) TABLESPACE "fast_ssd";
            CREATE INDEX IF NOT EXISTS "ix_invoices_user" ON "billing"."invoices" ("user_id");
            """;

        var script2 = """
            ALTER TABLE "billing"."invoices" ADD COLUMN IF NOT EXISTS "notes" text NULL;
            DROP TABLE IF EXISTS "billing"."legacy_invoices";
            DROP INDEX IF EXISTS "billing"."ix_old";
            """;

        var req = SqlPermissionRequirementExtractor.Extract([script1, script2], options, dialect, historyTableExists: false);

        Assert.True(req.RequiresHistoryTableCreation);
        Assert.Contains("billing", req.SchemasToCreate);
        Assert.Contains("public", req.TableCreateSchemas);
        Assert.Contains("billing", req.TableCreateSchemas);
        Assert.Contains("billing", req.SequenceCreateSchemas);
        Assert.Contains("billing", req.TypeSchemas);
        Assert.Contains(new QualifiedObjectName("billing", "invoices"), req.TableAlters);
        Assert.Contains(new QualifiedObjectName("billing", "legacy_invoices"), req.TableDrops);
        Assert.Contains(new QualifiedObjectName("billing", "invoices"), req.IndexCreateTables);
        Assert.Contains("billing", req.IndexDropSchemas);
        Assert.Contains("pg_trgm", req.Extensions);
        Assert.Contains("fast_ssd", req.Tablespaces);
        Assert.Contains(req.ForeignKeyReferences, f =>
            f.PrincipalSchema == "access_control" &&
            f.PrincipalTable == "users" &&
            f.ConstraintName == "fk_invoices_users");
    }

    [Fact]
    public void Extractor_UnwrapsOracleExecuteImmediateBlocks()
    {
        var options = new DbMigratorOptions
        {
            Dialect = SqlDialect.Oracle,
            HistoryTableName = "__db_migrator_logs"
        };
        var dialect = MigrationDialectRegistry.Get(options);

        var oracleScript = """
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE "BILLING"."INVOICES" ("ID" NUMBER(19) NOT NULL) TABLESPACE "USERS"';
            EXCEPTION
                WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF;
            END;
            /
            BEGIN
                EXECUTE IMMEDIATE 'ALTER TABLE "BILLING"."INVOICES" ADD CONSTRAINT "FK_INV_USER" FOREIGN KEY ("USER_ID") REFERENCES "CORE"."USERS" ("ID")';
            END;
            /
            BEGIN
                EXECUTE IMMEDIATE 'CREATE SEQUENCE "BILLING"."INV_SEQ" START WITH 1';
            END;
            /
            """;

        var req = SqlPermissionRequirementExtractor.Extract([oracleScript], options, dialect, historyTableExists: true);

        Assert.False(req.RequiresHistoryTableCreation);
        Assert.Contains("BILLING", req.TableCreateSchemas);
        Assert.Contains("BILLING", req.SequenceCreateSchemas);
        Assert.Contains(new QualifiedObjectName("BILLING", "INVOICES"), req.TableAlters);
        Assert.Contains("USERS", req.Tablespaces);
        Assert.Contains(req.ForeignKeyReferences, f =>
            f.PrincipalSchema == "CORE" &&
            f.PrincipalTable == "USERS" &&
            f.ConstraintName == "FK_INV_USER");
    }
}
