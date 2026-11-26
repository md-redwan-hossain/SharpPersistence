using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli.Ui;

internal static partial class UiDdlCatalogChecker
{
    public static IReadOnlyList<UiDdlStatementDto> ParseScriptDdls(string sql, SqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        var statements = SplitTopLevelStatements(sql, dialect);
        var result = new List<UiDdlStatementDto>();

        foreach (var rawStmt in statements)
        {
            var trimmed = rawStmt.Trim();
            if (trimmed.Length == 0)
                continue;

            var clean = StripLeadingComments(trimmed);
            if (clean.Length == 0 ||
                clean.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("BEGIN;", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("COMMIT", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseDdl(clean, trimmed, result.Count, out var dto))
            {
                result.Add(dto);
            }
        }

        return result;
    }

    private static bool TryParseDdl(string clean, string originalSql, int index, [NotNullWhen(true)] out UiDdlStatementDto? dto)
    {
        dto = null;
        Match m;

        if ((m = CreateSchemaRx().Match(clean)).Success)
        {
            var schemaName = Unquote(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "CREATE", "Schema", schemaName, null, schemaName, schemaName, originalSql);
            return true;
        }

        if ((m = DropSchemaRx().Match(clean)).Success)
        {
            var schemaName = Unquote(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Schema", schemaName, null, schemaName, schemaName, originalSql);
            return true;
        }

        if ((m = CreateTableRx().Match(clean)).Success)
        {
            var (schema, name) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "CREATE", "Table", schema, name, name, FormatTarget(schema, name), originalSql);
            return true;
        }

        if ((m = DropTableRx().Match(clean)).Success)
        {
            var (schema, name) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Table", schema, name, name, FormatTarget(schema, name), originalSql);
            return true;
        }

        if ((m = AddConstraintRx().Match(clean)).Success)
        {
            var (schema, table) = SplitQualified(m.Groups[1].Value);
            var con = Unquote(m.Groups[2].Value);
            dto = new UiDdlStatementDto(index, "ADD", "Constraint", schema, table, con, con, originalSql);
            return true;
        }

        if ((m = DropConstraintRx().Match(clean)).Success)
        {
            var (schema, table) = SplitQualified(m.Groups[1].Value);
            var con = Unquote(m.Groups[2].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Constraint", schema, table, con, con, originalSql);
            return true;
        }

        if ((m = AddColumnRx().Match(clean)).Success)
        {
            var (schema, table) = SplitQualified(m.Groups[1].Value);
            var col = Unquote(m.Groups[2].Value);
            dto = new UiDdlStatementDto(index, "ADD", "Column", schema, table, col, $"{FormatTarget(schema, table)}.{col}", originalSql);
            return true;
        }

        if ((m = DropColumnRx().Match(clean)).Success)
        {
            var (schema, table) = SplitQualified(m.Groups[1].Value);
            var col = Unquote(m.Groups[2].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Column", schema, table, col, $"{FormatTarget(schema, table)}.{col}", originalSql);
            return true;
        }

        if ((m = CreateIndexRx().Match(clean)).Success)
        {
            var idx = Unquote(m.Groups[1].Value);
            var (schema, table) = SplitQualified(m.Groups[2].Value);
            dto = new UiDdlStatementDto(index, "CREATE", "Index", schema, table, idx, idx, originalSql);
            return true;
        }

        if ((m = DropIndexRx().Match(clean)).Success)
        {
            var (schema, idx) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Index", schema, null, idx, FormatTarget(schema, idx), originalSql);
            return true;
        }

        if ((m = CreateSequenceRx().Match(clean)).Success)
        {
            var (schema, seq) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "CREATE", "Sequence", schema, null, seq, FormatTarget(schema, seq), originalSql);
            return true;
        }

        if ((m = DropSequenceRx().Match(clean)).Success)
        {
            var (schema, seq) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Sequence", schema, null, seq, FormatTarget(schema, seq), originalSql);
            return true;
        }

        if ((m = CreateExtensionRx().Match(clean)).Success)
        {
            var ext = Unquote(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "CREATE", "Extension", null, null, ext, ext, originalSql);
            return true;
        }

        if ((m = DropExtensionRx().Match(clean)).Success)
        {
            var ext = Unquote(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Extension", null, null, ext, ext, originalSql);
            return true;
        }

        if ((m = CreateTypeRx().Match(clean)).Success)
        {
            var (schema, typeName) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "CREATE", "Type", schema, null, typeName, FormatTarget(schema, typeName), originalSql);
            return true;
        }

        if ((m = DropTypeRx().Match(clean)).Success)
        {
            var (schema, typeName) = SplitQualified(m.Groups[1].Value);
            dto = new UiDdlStatementDto(index, "DROP", "Type", schema, null, typeName, FormatTarget(schema, typeName), originalSql);
            return true;
        }

        return false;
    }

    public static async Task<UiDdlCheckResultDto> CheckSingleAsync(
        DbMigratorOptions options,
        MigrationConnectionOpener opener,
        UiDdlCheckRequestDto req,
        CancellationToken ct)
    {
        DbConnection? conn = null;
        var owns = false;

        try
        {
            (conn, owns) = await opener.OpenAsync(ct).ConfigureAwait(false);
            return await EvaluateObjectAsync(conn, options.Dialect, req, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var root = MigrationDoctor.UnwrapException(ex);
            return new UiDdlCheckResultDto(null, req.Target, false, false, root.Message, root.Message);
        }
        finally
        {
            if (owns && conn is not null)
                await conn.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static async Task<UiScriptLiveCheckDto> CheckScriptAsync(
        DbMigratorOptions options,
        MigrationConnectionOpener opener,
        string scriptId,
        string sql,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var statements = ParseScriptDdls(sql, options.Dialect);
        var historyStore = new MigrationHistoryStore(options);

        DbConnection? conn = null;
        var owns = false;

        try
        {
            (conn, owns) = await opener.OpenAsync(ct).ConfigureAwait(false);

            bool? isApplied = null;
            if (Guid.TryParse(scriptId, out var parsedId) &&
                await historyStore.HistoryTableExistsAsync(conn, ct).ConfigureAwait(false))
            {
                isApplied = await CheckScriptAppliedAsync(conn, options, parsedId, ct).ConfigureAwait(false);
            }

            List<UiDdlCheckResultDto> results;
            if (options.Dialect == SqlDialect.PostgreSql && statements.Count > 1)
            {
                results = await EvaluatePostgresBatchAsync(conn, statements, ct).ConfigureAwait(false);
            }
            else
            {
                results = new List<UiDdlCheckResultDto>(statements.Count);
                foreach (var stmt in statements)
                {
                    var req = new UiDdlCheckRequestDto(stmt.Action, stmt.ObjectType, stmt.Schema, stmt.TableName, stmt.ObjectName, stmt.Target);
                    results.Add(await EvaluateObjectAsync(conn, options.Dialect, req, stmt.Index, ct).ConfigureAwait(false));
                }
            }

            sw.Stop();
            return new UiScriptLiveCheckDto(scriptId, true, isApplied, sw.ElapsedMilliseconds, null, results);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var root = MigrationDoctor.UnwrapException(ex);
            return new UiScriptLiveCheckDto(scriptId, false, null, sw.ElapsedMilliseconds, root.Message, []);
        }
        finally
        {
            if (owns && conn is not null)
                await conn.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<bool> CheckScriptAppliedAsync(
        DbConnection conn,
        DbMigratorOptions options,
        Guid scriptId,
        CancellationToken ct)
    {
        var table = SqlFormatHelper.Qualify(options.HistorySchema, options.HistoryTableName);
        await using var cmd = conn.CreateCommand();
        if (options.Dialect == SqlDialect.Oracle)
        {
            OracleCommandHelper.EnableBindByName(cmd);
            cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE \"script_id\" = :id";
            AddParam(cmd, "id", scriptId.ToString("N"));
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
            return count > 0;
        }

        cmd.CommandText = $"SELECT EXISTS (SELECT 1 FROM {table} WHERE \"script_id\" = @id)";
        AddParam(cmd, "@id", scriptId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }

    private static async Task<UiDdlCheckResultDto> EvaluateObjectAsync(
        DbConnection conn,
        SqlDialect dialect,
        UiDdlCheckRequestDto req,
        int? index,
        CancellationToken ct)
    {
        var exists = dialect == SqlDialect.Oracle
            ? await CheckOracleExistsAsync(conn, req, ct).ConfigureAwait(false)
            : await CheckPostgresExistsAsync(conn, req, ct).ConfigureAwait(false);

        var isDrop = string.Equals(req.Action, "DROP", StringComparison.OrdinalIgnoreCase);
        var passed = isDrop ? !exists : exists;
        var detail = exists
            ? $"{req.ObjectType} '{req.Target}' exists in database."
            : $"{req.ObjectType} '{req.Target}' does not exist in database.";

        return new UiDdlCheckResultDto(index, req.Target, passed, exists, detail, null);
    }

    private static async Task<bool> CheckPostgresExistsAsync(DbConnection conn, UiDdlCheckRequestDto req, CancellationToken ct)
    {
        var schema = string.IsNullOrWhiteSpace(req.Schema) ? "public" : req.Schema;
        await using var cmd = conn.CreateCommand();

        switch (req.ObjectType)
        {
            case "Schema":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = @n)";
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Table":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @s AND c.relname = @n AND c.relkind IN ('r','p'))";
                AddParam(cmd, "@s", schema);
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Column":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid = a.attrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @s AND c.relname = @t AND a.attname = @n AND a.attnum > 0 AND NOT a.attisdropped)";
                AddParam(cmd, "@s", schema);
                AddParam(cmd, "@t", req.TableName ?? string.Empty);
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Index":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @s AND c.relname = @n AND c.relkind IN ('i','I'))";
                AddParam(cmd, "@s", schema);
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Constraint":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint con JOIN pg_catalog.pg_class c ON c.oid = con.conrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @s AND c.relname = @t AND con.conname = @n)";
                AddParam(cmd, "@s", schema);
                AddParam(cmd, "@t", req.TableName ?? string.Empty);
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Sequence":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = @s AND c.relname = @n AND c.relkind = 'S')";
                AddParam(cmd, "@s", schema);
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Extension":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_extension WHERE extname = @n)";
                AddParam(cmd, "@n", req.ObjectName);
                break;
            case "Type":
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = @s AND t.typname = @n)";
                AddParam(cmd, "@s", schema);
                AddParam(cmd, "@n", req.ObjectName);
                break;
            default:
                return false;
        }

        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }

    private static async Task<bool> CheckOracleExistsAsync(DbConnection conn, UiDdlCheckRequestDto req, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        OracleCommandHelper.EnableBindByName(cmd);
        var hasSchema = !string.IsNullOrWhiteSpace(req.Schema);

        switch (req.ObjectType)
        {
            case "Schema":
                cmd.CommandText = "SELECT COUNT(*) FROM ALL_USERS WHERE USERNAME = :n";
                AddParam(cmd, "n", req.ObjectName);
                break;
            case "Table":
                cmd.CommandText = hasSchema
                    ? "SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = :s AND TABLE_NAME = :n"
                    : "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :n";
                if (hasSchema) AddParam(cmd, "s", req.Schema!);
                AddParam(cmd, "n", req.ObjectName);
                break;
            case "Column":
                cmd.CommandText = hasSchema
                    ? "SELECT COUNT(*) FROM ALL_TAB_COLUMNS WHERE OWNER = :s AND TABLE_NAME = :t AND COLUMN_NAME = :n"
                    : "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :t AND COLUMN_NAME = :n";
                if (hasSchema) AddParam(cmd, "s", req.Schema!);
                AddParam(cmd, "t", req.TableName ?? string.Empty);
                AddParam(cmd, "n", req.ObjectName);
                break;
            case "Index":
                cmd.CommandText = hasSchema
                    ? "SELECT COUNT(*) FROM ALL_INDEXES WHERE OWNER = :s AND INDEX_NAME = :n"
                    : "SELECT COUNT(*) FROM USER_INDEXES WHERE INDEX_NAME = :n";
                if (hasSchema) AddParam(cmd, "s", req.Schema!);
                AddParam(cmd, "n", req.ObjectName);
                break;
            case "Constraint":
                cmd.CommandText = hasSchema
                    ? "SELECT COUNT(*) FROM ALL_CONSTRAINTS WHERE OWNER = :s AND TABLE_NAME = :t AND CONSTRAINT_NAME = :n"
                    : "SELECT COUNT(*) FROM USER_CONSTRAINTS WHERE TABLE_NAME = :t AND CONSTRAINT_NAME = :n";
                if (hasSchema) AddParam(cmd, "s", req.Schema!);
                AddParam(cmd, "t", req.TableName ?? string.Empty);
                AddParam(cmd, "n", req.ObjectName);
                break;
            case "Sequence":
                cmd.CommandText = hasSchema
                    ? "SELECT COUNT(*) FROM ALL_SEQUENCES WHERE SEQUENCE_OWNER = :s AND SEQUENCE_NAME = :n"
                    : "SELECT COUNT(*) FROM USER_SEQUENCES WHERE SEQUENCE_NAME = :n";
                if (hasSchema) AddParam(cmd, "s", req.Schema!);
                AddParam(cmd, "n", req.ObjectName);
                break;
            default:
                return false;
        }

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static IEnumerable<string> SplitTopLevelStatements(string sql, SqlDialect dialect)
    {
        if (dialect == SqlDialect.Oracle)
        {
            foreach (var stmt in OracleMigrationDialect.SplitStatements(sql))
                yield return stmt;
            yield break;
        }

        // PostgreSQL: split on ';' outside single/double quotes, comments, and $$ dollar-quoted DO blocks
        var sb = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inDollar = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (!inSingle && !inDouble && c == '$' && i + 1 < sql.Length && sql[i + 1] == '$')
            {
                inDollar = !inDollar;
                sb.Append("$$");
                i++;
                continue;
            }

            if (!inDollar && !inDouble && c == '\'')
            {
                inSingle = !inSingle;
                sb.Append(c);
                continue;
            }

            if (!inDollar && !inSingle && c == '"')
            {
                inDouble = !inDouble;
                sb.Append(c);
                continue;
            }

            if (!inSingle && !inDouble && !inDollar && c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                    sb.Append(sql[i++]);
                if (i < sql.Length) sb.Append(sql[i]);
                continue;
            }

            sb.Append(c);
            if (!inSingle && !inDouble && !inDollar && c == ';')
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static string StripLeadingComments(string sql)
    {
        using var reader = new StringReader(sql);
        var sb = new StringBuilder(sql.Length);
        var skipping = true;
        while (reader.ReadLine() is { } line)
        {
            var t = line.Trim();
            if (skipping && (t.Length == 0 || t.StartsWith("--", StringComparison.Ordinal)))
                continue;
            skipping = false;
            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    private static (string? Schema, string Name) SplitQualified(string raw)
    {
        var dot = raw.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return (null, Unquote(raw));
        return (Unquote(raw[..dot]), Unquote(raw[(dot + 1)..]));
    }

    private static string Unquote(string raw) => raw.Trim().Trim('"');

    private static async Task<List<UiDdlCheckResultDto>> EvaluatePostgresBatchAsync(
        DbConnection conn,
        IReadOnlyList<UiDdlStatementDto> statements,
        CancellationToken ct)
    {
        var schemas = statements
            .Select(s => string.IsNullOrWhiteSpace(s.Schema) ? "public" : s.Schema)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var schemaIn = string.Join(", ", schemas.Select(s => $"'{SqlFormatHelper.EscapeLiteral(s)}'"));

        var existingSchemas = new HashSet<string>(StringComparer.Ordinal);
        var existingRelations = new HashSet<(string Schema, string Name, string Kind)>();
        var existingColumns = new HashSet<(string Schema, string Table, string Col)>();
        var existingConstraints = new HashSet<(string Schema, string Table, string Name)>();
        var existingExtensions = new HashSet<string>(StringComparer.Ordinal);
        var existingTypes = new HashSet<(string Schema, string Name)>();

        static string ToSqlIn(IEnumerable<string> values) =>
            string.Join(", ", values.Distinct(StringComparer.Ordinal).Select(v => $"'{SqlFormatHelper.EscapeLiteral(v)}'"));

        var schemaNames = statements.Where(s => s.ObjectType == "Schema").Select(s => s.ObjectName).ToList();
        if (schemaNames.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT nspname FROM pg_catalog.pg_namespace WHERE nspname IN ({ToSqlIn(schemaNames)})";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) existingSchemas.Add(r.GetString(0));
        }

        var relNames = statements.Where(s => s.ObjectType is "Table" or "Index" or "Sequence").Select(s => s.ObjectName).ToList();
        if (relNames.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT n.nspname, c.relname, c.relkind::text FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ({schemaIn}) AND c.relname IN ({ToSqlIn(relNames)}) AND c.relkind IN ('r','p','i','I','S')";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) existingRelations.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        var colStmts = statements.Where(s => s.ObjectType == "Column").ToList();
        if (colStmts.Count > 0)
        {
            var colTables = ToSqlIn(colStmts.Select(s => s.TableName ?? string.Empty));
            var colNames = ToSqlIn(colStmts.Select(s => s.ObjectName));
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT n.nspname, c.relname, a.attname FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid = a.attrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ({schemaIn}) AND c.relname IN ({colTables}) AND a.attname IN ({colNames}) AND a.attnum > 0 AND NOT a.attisdropped";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) existingColumns.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        var conNames = statements.Where(s => s.ObjectType == "Constraint").Select(s => s.ObjectName).ToList();
        if (conNames.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT n.nspname, c.relname, con.conname FROM pg_catalog.pg_constraint con JOIN pg_catalog.pg_class c ON c.oid = con.conrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ({schemaIn}) AND con.conname IN ({ToSqlIn(conNames)})";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) existingConstraints.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        var extNames = statements.Where(s => s.ObjectType == "Extension").Select(s => s.ObjectName).ToList();
        if (extNames.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT extname FROM pg_catalog.pg_extension WHERE extname IN ({ToSqlIn(extNames)})";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) existingExtensions.Add(r.GetString(0));
        }

        var typeNames = statements.Where(s => s.ObjectType == "Type").Select(s => s.ObjectName).ToList();
        if (typeNames.Count > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT n.nspname, t.typname FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname IN ({schemaIn}) AND t.typname IN ({ToSqlIn(typeNames)})";
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) existingTypes.Add((r.GetString(0), r.GetString(1)));
        }

        var results = new List<UiDdlCheckResultDto>(statements.Count);
        foreach (var s in statements)
        {
            var sc = string.IsNullOrWhiteSpace(s.Schema) ? "public" : s.Schema;
            var exists = s.ObjectType switch
            {
                "Schema" => existingSchemas.Contains(s.ObjectName),
                "Table" => existingRelations.Contains((sc, s.ObjectName, "r")) || existingRelations.Contains((sc, s.ObjectName, "p")),
                "Index" => existingRelations.Contains((sc, s.ObjectName, "i")) || existingRelations.Contains((sc, s.ObjectName, "I")),
                "Sequence" => existingRelations.Contains((sc, s.ObjectName, "S")),
                "Column" => existingColumns.Contains((sc, s.TableName ?? string.Empty, s.ObjectName)),
                "Constraint" => existingConstraints.Contains((sc, s.TableName ?? string.Empty, s.ObjectName)),
                "Extension" => existingExtensions.Contains(s.ObjectName),
                "Type" => existingTypes.Contains((sc, s.ObjectName)),
                _ => false
            };

            var isDrop = string.Equals(s.Action, "DROP", StringComparison.OrdinalIgnoreCase);
            var passed = isDrop ? !exists : exists;
            var detail = exists
                ? $"{s.ObjectType} '{s.Target}' exists in database."
                : $"{s.ObjectType} '{s.Target}' does not exist in database.";
            results.Add(new UiDdlCheckResultDto(s.Index, s.Target, passed, exists, detail, null));
        }

        return results;
    }

    private static string FormatTarget(string? schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";

    [GeneratedRegex(@"CREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateSchemaRx();

    [GeneratedRegex(@"DROP\s+SCHEMA\s+(?:IF\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex DropSchemaRx();

    [GeneratedRegex(@"CREATE\s+(?:UNLOGGED\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTableRx();

    [GeneratedRegex(@"DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex DropTableRx();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)\s+ADD\s+(?:COLUMN\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddColumnRx();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)\s+DROP\s+(?:COLUMN\s+)?(?:IF\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex DropColumnRx();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)\s+ADD\s+CONSTRAINT\s+(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex AddConstraintRx();

    [GeneratedRegex(@"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)\s+DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex DropConstraintRx();

    [GeneratedRegex(@"CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(""[^""]+""|\w+)\s+ON\s+((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateIndexRx();

    [GeneratedRegex(@"DROP\s+INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex DropIndexRx();

    [GeneratedRegex(@"CREATE\s+(?:OR\s+REPLACE\s+)?SEQUENCE\s+(?:IF\s+NOT\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateSequenceRx();

    [GeneratedRegex(@"DROP\s+SEQUENCE\s+(?:IF\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex DropSequenceRx();

    [GeneratedRegex(@"CREATE\s+EXTENSION\s+(?:IF\s+NOT\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateExtensionRx();

    [GeneratedRegex(@"DROP\s+EXTENSION\s+(?:IF\s+EXISTS\s+)?(""[^""]+""|\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex DropExtensionRx();

    [GeneratedRegex(@"CREATE\s+TYPE\s+((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTypeRx();

    [GeneratedRegex(@"DROP\s+TYPE\s+(?:IF\s+EXISTS\s+)?((?:""[^""]+""|\w+)(?:\s*\.\s*(?:""[^""]+""|\w+))?)", RegexOptions.IgnoreCase)]
    private static partial Regex DropTypeRx();
}
