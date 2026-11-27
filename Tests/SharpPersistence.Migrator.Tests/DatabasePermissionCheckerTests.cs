using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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
            HistorySchema = "public"
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
            Dialect = SqlDialect.Oracle
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

    [Fact]
    public void Extractor_IgnoresDdlKeywordsInsideStringLiterals_AndPairsFkPerStatement()
    {
        var options = new DbMigratorOptions { Dialect = SqlDialect.PostgreSql };
        var dialect = MigrationDialectRegistry.Get(options);

        var sql = """
            INSERT INTO "logs" ("msg") VALUES ('FOREIGN KEY ("x") REFERENCES "secret"."vault" ("id") TABLESPACE "fake_ts"');
            ALTER TABLE "sales"."orders" ADD CONSTRAINT "fk_orders_users" FOREIGN KEY ("user_id") REFERENCES "core"."users" ("id");
            ALTER TABLE "billing"."invoices" ADD CONSTRAINT "fk_inv_orders" FOREIGN KEY ("order_id") REFERENCES "sales"."orders" ("id");
            """;

        var req = SqlPermissionRequirementExtractor.Extract([sql], options, dialect, historyTableExists: true);

        Assert.DoesNotContain("fake_ts", req.Tablespaces);
        Assert.DoesNotContain(req.ForeignKeyReferences, f => f.PrincipalTable == "vault");
        Assert.Contains(req.ForeignKeyReferences, f =>
            f.DependentSchema == "sales" && f.PrincipalSchema == "core" && f.PrincipalTable == "users");
        Assert.Contains(req.ForeignKeyReferences, f =>
            f.DependentSchema == "billing" && f.PrincipalSchema == "sales" && f.PrincipalTable == "orders");
    }

    [Fact]
    public void OracleSplitStatements_HandlesProceduresTriggersAndTrailingComments()
    {
        var sql = """
            DROP TABLE "OLD_LOG"; -- cleanup old table
            CREATE OR REPLACE TRIGGER "TRG_AUDIT"
            BEFORE INSERT ON "ORDERS"
            FOR EACH ROW
            BEGIN
                :NEW."CREATED_AT" := SYSTIMESTAMP;
            END;
            /
            CREATE TABLE "NEW_LOG" ("ID" NUMBER(19));
            """;

        var stmts = OracleMigrationDialect.SplitStatements(sql).ToList();

        Assert.Equal(3, stmts.Count);
        Assert.Equal("""DROP TABLE "OLD_LOG" """.Trim(), stmts[0]);
        Assert.Contains("""BEFORE INSERT ON "ORDERS" """.Trim(), stmts[1]);
        Assert.Contains(""":NEW."CREATED_AT" := SYSTIMESTAMP;""", stmts[1]);
        Assert.Equal("""CREATE TABLE "NEW_LOG" ("ID" NUMBER(19))""", stmts[2]);
    }

    [Fact]
    public async Task OraclePermissionChecker_FlagsMissingSchemaPrivsQuotaAndDirectReferences()
    {
        var req = new DatabasePermissionRequirements { HistoryTableName = "__db_migrator_logs" };
        req.SchemasTouched.Add("BILLING");
        req.SchemasTouched.Add("CORE");
        req.TableCreateSchemas.Add("BILLING");
        req.TableCreateSchemas.Add("");
        req.ForeignKeyReferences.Add(new ForeignKeyReferenceRequirement("BILLING", "CORE", "USERS", "FK_INV_USERS"));

        await using var conn = new FakeOracleConnection(sql =>
        {
            if (sql.Contains("FROM USER_USERS", StringComparison.OrdinalIgnoreCase))
                return [["APP_USER", "USERS"]];
            if (sql.Contains("FROM SESSION_PRIVS", StringComparison.OrdinalIgnoreCase))
                return [["CREATE TABLE"]];
            if (sql.Contains("FROM ALL_USERS", StringComparison.OrdinalIgnoreCase))
                return [["CORE"]]; // BILLING missing
            if (sql.Contains("FROM USER_TS_QUOTAS", StringComparison.OrdinalIgnoreCase))
                return [["USERS", 100L, 100L]]; // exhausted quota
            if (sql.Contains("FROM ALL_TABLES", StringComparison.OrdinalIgnoreCase))
                return [["CORE", "USERS"]];
            if (sql.Contains("FROM USER_TAB_PRIVS_RECD", StringComparison.OrdinalIgnoreCase))
                return [];
            return [];
        });

        var checks = await OraclePermissionChecker.CheckAsync(conn, req, TestContext.Current.CancellationToken);

        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("Schema 'BILLING' does not exist"));
        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("CREATE ANY TABLE"));
        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("ORA-01950"));
        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("direct REFERENCES privilege on 'CORE.USERS'"));
    }

    private sealed class FakeOracleConnection(Func<string, List<object[]>> queryHandler) : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "ORCL";
        public override string DataSource => "fake";
        public override string ServerVersion => "19.0";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeOracleCommand(queryHandler);
    }

    private sealed class FakeOracleCommand(Func<string, List<object[]>> queryHandler) : DbCommand
    {
        private readonly FakeParamCollection _params = new();
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _params;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeParam();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => new FakeReader(queryHandler(CommandText));
    }

    private sealed class FakeParam : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        public override int Size { get; set; }
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeParamCollection : DbParameterCollection
    {
        private readonly List<object> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add(value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) _items.Add(v!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains(value);
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf(value);
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => _items.Insert(index, value);
        public override void Remove(object value) => _items.Remove(value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) { }
        protected override DbParameter GetParameter(int index) => (DbParameter)_items[index];
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) { }
    }

    private sealed class FakeReader(List<object[]> rows) : DbDataReader
    {
        private int _index = -1;
        public override int FieldCount => rows.Count > 0 ? rows[0].Length : 0;
        public override bool HasRows => rows.Count > 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int Depth => 0;
        public override object this[int ordinal] => rows[_index][ordinal];
        public override object this[string name] => throw new NotSupportedException();
        public override bool Read() => ++_index < rows.Count;
        public override string GetString(int ordinal) => (string)rows[_index][ordinal];
        public override object GetValue(int ordinal) => rows[_index][ordinal];
        public override bool IsDBNull(int ordinal) => rows[_index][ordinal] is null or DBNull;
        public override bool GetBoolean(int ordinal) => (bool)rows[_index][ordinal];
        public override byte GetByte(int ordinal) => Convert.ToByte(rows[_index][ordinal]);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => Convert.ToChar(rows[_index][ordinal]);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => string.Empty;
        public override DateTime GetDateTime(int ordinal) => (DateTime)rows[_index][ordinal];
        public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(rows[_index][ordinal]);
        public override double GetDouble(int ordinal) => Convert.ToDouble(rows[_index][ordinal]);
        public override IEnumerator GetEnumerator() => rows.GetEnumerator();
        public override Type GetFieldType(int ordinal) => rows[_index][ordinal].GetType();
        public override float GetFloat(int ordinal) => Convert.ToSingle(rows[_index][ordinal]);
        public override Guid GetGuid(int ordinal) => (Guid)rows[_index][ordinal];
        public override short GetInt16(int ordinal) => Convert.ToInt16(rows[_index][ordinal]);
        public override int GetInt32(int ordinal) => Convert.ToInt32(rows[_index][ordinal]);
        public override long GetInt64(int ordinal) => Convert.ToInt64(rows[_index][ordinal]);
        public override string GetName(int ordinal) => string.Empty;
        public override int GetOrdinal(string name) => -1;
        public override int GetValues(object[] values) => 0;
        public override bool NextResult() => false;
    }
}
