using System.Globalization;
using System.Text;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Dialects;

public sealed class OracleDialectGenerator : ISqlDialectGenerator
{
    private readonly OracleDbCompatibility _compatibility;

    public OracleDialectGenerator(OracleDbCompatibility compatibility = OracleDbCompatibility.OracleDb19)
    {
        _compatibility = compatibility;
    }

    private bool SupportsIfExists => _compatibility >= OracleDbCompatibility.OracleDb23;

    public string GenerateSql(IReadOnlyList<SchemaOperation> ops)
    {
        if (ops.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (var op in ops)
        {
            switch (op)
            {
                case EnsureExtensionOp or DropExtensionOp or CreateEnumOp or AddEnumValueOp or DropEnumOp
                    or AddExclusionConstraintOp or DropExclusionConstraintOp:
                    throw new NotSupportedException($"Operation '{op.GetType().Name}' is PostgreSQL-specific and not supported on Oracle.");

                case EnsureSchemaOp s:
                    sb.AppendLine($"-- Oracle schemas map to database users; ensure user {Quote(s.SchemaName)} exists.");
                    sb.AppendLine();
                    break;

                case CreateSequenceOp cs:
                    AppendCreateSequence(sb, cs.Sequence);
                    sb.AppendLine();
                    break;

                case AlterSequenceOp alts:
                    AppendAlterSequence(sb, alts);
                    sb.AppendLine();
                    break;

                case DropSequenceOp ds:
                    if (SupportsIfExists)
                    {
                        sb.AppendLine($"DROP SEQUENCE IF EXISTS {Qualify(ds.Schema, ds.Name)};");
                    }
                    else
                    {
                        AppendCatalogGuardedDdl(
                            sb,
                            BuildSequenceExistsQuery(ds.Schema, ds.Name),
                            executeWhenExists: true,
                            $"DROP SEQUENCE {Qualify(ds.Schema, ds.Name)}");
                    }
                    sb.AppendLine();
                    break;

                case CreateTableOp c:
                    AppendCreateTable(sb, c.Table);
                    sb.AppendLine();
                    break;

                case DropTableOp d:
                    if (SupportsIfExists)
                    {
                        sb.AppendLine($"DROP TABLE IF EXISTS {Qualify(d.Schema, d.TableName)} CASCADE CONSTRAINTS PURGE;");
                    }
                    else
                    {
                        AppendCatalogGuardedDdl(
                            sb,
                            BuildTableExistsQuery(d.Schema, d.TableName),
                            executeWhenExists: true,
                            $"DROP TABLE {Qualify(d.Schema, d.TableName)} CASCADE CONSTRAINTS PURGE");
                    }
                    sb.AppendLine();
                    break;

                case RenameTableOp rt:
                    sb.AppendLine($"ALTER TABLE {Qualify(rt.OldSchema, rt.OldTableName)} RENAME TO {Quote(rt.NewTableName)};");
                    sb.AppendLine();
                    break;

                case DropIndexOp di:
                    if (SupportsIfExists)
                    {
                        sb.AppendLine($"DROP INDEX IF EXISTS {Qualify(di.Schema, di.Name)};");
                    }
                    else
                    {
                        AppendCatalogGuardedDdl(
                            sb,
                            BuildIndexExistsQuery(di.Schema, di.Name),
                            executeWhenExists: true,
                            $"DROP INDEX {Qualify(di.Schema, di.Name)}");
                    }
                    sb.AppendLine();
                    break;

                case RenameIndexOp ri:
                    sb.AppendLine($"ALTER INDEX {Qualify(ri.Schema, ri.OldName)} RENAME TO {Quote(ri.NewName)};");
                    sb.AppendLine();
                    break;

                case CreateIndexOp ci:
                    AppendCreateIndex(sb, ci);
                    sb.AppendLine();
                    break;

                case SetTableCommentOp stc:
                    sb.AppendLine($"COMMENT ON TABLE {Qualify(stc.Schema, stc.TableName)} IS '{EscapeLiteral(stc.Comment ?? string.Empty)}';");
                    sb.AppendLine();
                    break;

                case SetColumnCommentOp scc:
                    sb.AppendLine($"COMMENT ON COLUMN {Qualify(scc.Schema, scc.TableName)}.{Quote(scc.ColumnName)} IS '{EscapeLiteral(scc.Comment ?? string.Empty)}';");
                    sb.AppendLine();
                    break;

                case DropPrimaryKeyOp dpk:
                    AppendDropConstraint(sb, dpk.Schema, dpk.TableName, dpk.Name);
                    sb.AppendLine();
                    break;

                case AddPrimaryKeyOp apk:
                    var pkCols = string.Join(", ", apk.PrimaryKey.ColumnNames.Select(Quote));
                    if (SupportsIfExists)
                    {
                        sb.AppendLine($"ALTER TABLE {Qualify(apk.Schema, apk.TableName)} ADD CONSTRAINT IF NOT EXISTS {Quote(apk.PrimaryKey.Name)} PRIMARY KEY ({pkCols});");
                    }
                    else
                    {
                        AppendCatalogGuardedDdl(
                            sb,
                            BuildConstraintExistsQuery(apk.Schema, apk.TableName, apk.PrimaryKey.Name),
                            executeWhenExists: false,
                            $"ALTER TABLE {Qualify(apk.Schema, apk.TableName)} ADD CONSTRAINT {Quote(apk.PrimaryKey.Name)} PRIMARY KEY ({pkCols})");
                    }
                    sb.AppendLine();
                    break;

                case DropCheckConstraintOp dck:
                    AppendDropConstraint(sb, dck.Schema, dck.TableName, dck.Name);
                    sb.AppendLine();
                    break;

                case AddCheckConstraintOp ack:
                    if (SupportsIfExists)
                    {
                        sb.AppendLine($"ALTER TABLE {Qualify(ack.Schema, ack.TableName)} ADD CONSTRAINT IF NOT EXISTS {Quote(ack.Constraint.Name)} CHECK ({ack.Constraint.Sql});");
                    }
                    else
                    {
                        AppendCatalogGuardedDdl(
                            sb,
                            BuildConstraintExistsQuery(ack.Schema, ack.TableName, ack.Constraint.Name),
                            executeWhenExists: false,
                            $"ALTER TABLE {Qualify(ack.Schema, ack.TableName)} ADD CONSTRAINT {Quote(ack.Constraint.Name)} CHECK ({ack.Constraint.Sql})");
                    }
                    sb.AppendLine();
                    break;

                case DropForeignKeyOp dfk:
                    AppendDropConstraint(sb, dfk.Schema, dfk.TableName, dfk.Name);
                    sb.AppendLine();
                    break;

                case AddForeignKeyOp afk:
                    AppendAddForeignKey(sb, afk);
                    sb.AppendLine();
                    break;

                case RenameConstraintOp rc:
                    sb.AppendLine($"ALTER TABLE {Qualify(rc.Schema, rc.TableName)} RENAME CONSTRAINT {Quote(rc.OldName)} TO {Quote(rc.NewName)};");
                    sb.AppendLine();
                    break;

                case AddColumnOp ac:
                    AppendAddColumn(sb, ac);
                    sb.AppendLine();
                    break;

                case DropColumnOp dc:
                    if (SupportsIfExists)
                    {
                        sb.AppendLine($"ALTER TABLE {Qualify(dc.Schema, dc.TableName)} DROP COLUMN IF EXISTS {Quote(dc.ColumnName)};");
                    }
                    else
                    {
                        AppendCatalogGuardedDdl(
                            sb,
                            BuildColumnExistsQuery(dc.Schema, dc.TableName, dc.ColumnName),
                            executeWhenExists: true,
                            $"ALTER TABLE {Qualify(dc.Schema, dc.TableName)} DROP COLUMN {Quote(dc.ColumnName)}");
                    }
                    sb.AppendLine();
                    break;

                case RenameColumnOp rnc:
                    sb.AppendLine($"ALTER TABLE {Qualify(rnc.Schema, rnc.TableName)} RENAME COLUMN {Quote(rnc.OldName)} TO {Quote(rnc.NewName)};");
                    sb.AppendLine();
                    break;

                case AlterColumnOp alc:
                    AppendAlterColumn(sb, alc);
                    sb.AppendLine();
                    break;
            }
        }

        return sb.ToString();
    }

    private void AppendCreateSequence(StringBuilder sb, SequenceSchema seq)
    {
        var opts = new List<string>
        {
            $"START WITH {seq.StartValue.ToString(CultureInfo.InvariantCulture)}",
            $"INCREMENT BY {seq.IncrementBy.ToString(CultureInfo.InvariantCulture)}"
        };
        if (seq.MinValue.HasValue)
        {
            opts.Add($"MINVALUE {seq.MinValue.Value.ToString(CultureInfo.InvariantCulture)}");
        }
        if (seq.MaxValue.HasValue)
        {
            opts.Add($"MAXVALUE {seq.MaxValue.Value.ToString(CultureInfo.InvariantCulture)}");
        }
        opts.Add(seq.IsCyclic ? "CYCLE" : "NOCYCLE");

        var optsSql = string.Join(" ", opts);
        if (SupportsIfExists)
        {
            sb.AppendLine($"CREATE SEQUENCE IF NOT EXISTS {Qualify(seq.Schema, seq.Name)} {optsSql};");
        }
        else
        {
            AppendCatalogGuardedDdl(
                sb,
                BuildSequenceExistsQuery(seq.Schema, seq.Name),
                executeWhenExists: false,
                $"CREATE SEQUENCE {Qualify(seq.Schema, seq.Name)} {optsSql}");
        }
    }

    private static void AppendAlterSequence(StringBuilder sb, AlterSequenceOp op)
    {
        var parts = new List<string>();
        if (op.OldSeq.IncrementBy != op.NewSeq.IncrementBy)
        {
            parts.Add($"INCREMENT BY {op.NewSeq.IncrementBy.ToString(CultureInfo.InvariantCulture)}");
        }
        if (op.OldSeq.MinValue != op.NewSeq.MinValue)
        {
            parts.Add(op.NewSeq.MinValue.HasValue
                ? $"MINVALUE {op.NewSeq.MinValue.Value.ToString(CultureInfo.InvariantCulture)}"
                : "NOMINVALUE");
        }
        if (op.OldSeq.MaxValue != op.NewSeq.MaxValue)
        {
            parts.Add(op.NewSeq.MaxValue.HasValue
                ? $"MAXVALUE {op.NewSeq.MaxValue.Value.ToString(CultureInfo.InvariantCulture)}"
                : "NOMAXVALUE");
        }
        if (op.OldSeq.IsCyclic != op.NewSeq.IsCyclic)
        {
            parts.Add(op.NewSeq.IsCyclic ? "CYCLE" : "NOCYCLE");
        }

        if (parts.Count > 0)
        {
            sb.AppendLine($"ALTER SEQUENCE {Qualify(op.NewSeq.Schema, op.NewSeq.Name)} {string.Join(" ", parts)};");
        }
    }

    private void AppendCreateTable(StringBuilder sb, TableSchema table)
    {
        var ddl = new StringBuilder();
        var ifNotExists = SupportsIfExists ? "IF NOT EXISTS " : string.Empty;
        ddl.AppendLine($"CREATE TABLE {ifNotExists}{Qualify(table.Schema, table.TableName)} (");

        var lines = new List<string>();
        foreach (var col in table.Columns.Where(c => !c.IsSystemColumn))
        {
            lines.Add($"    {FormatColumnDefinition(col)}");
        }

        if (table.PrimaryKey is not null && table.PrimaryKey.ColumnNames.Count > 0)
        {
            var pkCols = string.Join(", ", table.PrimaryKey.ColumnNames.Select(Quote));
            lines.Add($"    CONSTRAINT {Quote(table.PrimaryKey.Name)} PRIMARY KEY ({pkCols})");
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var comma = i < lines.Count - 1 ? "," : string.Empty;
            ddl.AppendLine($"{lines[i]}{comma}");
        }

        ddl.Append(")");

        if (SupportsIfExists)
        {
            sb.AppendLine($"{ddl};");
        }
        else
        {
            AppendCatalogGuardedDdl(
                sb,
                BuildTableExistsQuery(table.Schema, table.TableName),
                executeWhenExists: false,
                ddl.ToString());
        }
    }

    private void AppendAddColumn(StringBuilder sb, AddColumnOp ac)
    {
        var colDef = FormatColumnDefinition(ac.Column);
        if (SupportsIfExists)
        {
            sb.AppendLine($"ALTER TABLE {Qualify(ac.Schema, ac.TableName)} ADD IF NOT EXISTS ({colDef});");
        }
        else
        {
            AppendCatalogGuardedDdl(
                sb,
                BuildColumnExistsQuery(ac.Schema, ac.TableName, ac.Column.ColumnName),
                executeWhenExists: false,
                $"ALTER TABLE {Qualify(ac.Schema, ac.TableName)} ADD ({colDef})");
        }
    }

    private static void AppendAlterColumn(StringBuilder sb, AlterColumnOp alc)
    {
        var parts = new List<string> { Quote(alc.NewCol.ColumnName) };

        if (!string.Equals(alc.OldCol.SqlType, alc.NewCol.SqlType, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(alc.NewCol.SqlType);
        }

        if (!string.Equals(alc.OldCol.DefaultValueSql, alc.NewCol.DefaultValueSql, StringComparison.Ordinal))
        {
            parts.Add(string.IsNullOrWhiteSpace(alc.NewCol.DefaultValueSql)
                ? "DEFAULT NULL"
                : $"DEFAULT {NormalizeOracleDefault(alc.NewCol.DefaultValueSql, alc.NewCol.SqlType)}");
        }

        if (alc.OldCol.IsNullable != alc.NewCol.IsNullable)
        {
            parts.Add(alc.NewCol.IsNullable ? "NULL" : "NOT NULL");
        }

        if (parts.Count > 1)
        {
            sb.AppendLine($"ALTER TABLE {Qualify(alc.Schema, alc.TableName)} MODIFY ({string.Join(" ", parts)});");
        }
    }

    private void AppendAddForeignKey(StringBuilder sb, AddForeignKeyOp afk)
    {
        var clause = FormatForeignKeyClause(afk.ForeignKey);
        if (SupportsIfExists)
        {
            sb.AppendLine($"ALTER TABLE {Qualify(afk.Schema, afk.TableName)} ADD IF NOT EXISTS {clause};");
        }
        else
        {
            AppendCatalogGuardedDdl(
                sb,
                BuildConstraintExistsQuery(afk.Schema, afk.TableName, afk.ForeignKey.Name),
                executeWhenExists: false,
                $"ALTER TABLE {Qualify(afk.Schema, afk.TableName)} ADD {clause}");
        }
    }

    private void AppendCreateIndex(StringBuilder sb, CreateIndexOp ci)
    {
        var unique = ci.Index.IsUnique ? "UNIQUE " : string.Empty;
        var cols = string.Join(", ", ci.Index.ColumnNames.Select((c, idx) =>
        {
            var isDesc = ci.Index.IsDescending is not null &&
                         (ci.Index.IsDescending.Count == 1
                             ? ci.Index.IsDescending[0]
                             : idx < ci.Index.IsDescending.Count && ci.Index.IsDescending[idx]);
            return isDesc ? $"{Quote(c)} DESC" : Quote(c);
        }));

        var ifNotExists = SupportsIfExists ? "IF NOT EXISTS " : string.Empty;
        var indexName = Qualify(ci.Schema, ci.Index.Name);
        var stmt = $"CREATE {unique}INDEX {ifNotExists}{indexName} ON {Qualify(ci.Schema, ci.TableName)} ({cols})";

        if (SupportsIfExists)
        {
            sb.AppendLine($"{stmt};");
        }
        else
        {
            AppendCatalogGuardedDdl(
                sb,
                BuildIndexExistsQuery(ci.Schema, ci.Index.Name),
                executeWhenExists: false,
                stmt);
        }
    }

    private void AppendDropConstraint(StringBuilder sb, string? schema, string tableName, string constraintName)
    {
        if (SupportsIfExists)
        {
            sb.AppendLine($"ALTER TABLE {Qualify(schema, tableName)} DROP CONSTRAINT IF EXISTS {Quote(constraintName)};");
        }
        else
        {
            AppendCatalogGuardedDdl(
                sb,
                BuildConstraintExistsQuery(schema, tableName, constraintName),
                executeWhenExists: true,
                $"ALTER TABLE {Qualify(schema, tableName)} DROP CONSTRAINT {Quote(constraintName)}");
        }
    }

    private static string BuildTableExistsQuery(string? schema, string tableName) =>
        string.IsNullOrWhiteSpace(schema)
            ? $"SELECT COUNT(*) INTO v_cnt FROM USER_TABLES WHERE TABLE_NAME = '{EscapeLiteral(tableName)}';"
            : $"SELECT COUNT(*) INTO v_cnt FROM ALL_TABLES WHERE OWNER = '{EscapeLiteral(schema)}' AND TABLE_NAME = '{EscapeLiteral(tableName)}';";

    private static string BuildColumnExistsQuery(string? schema, string tableName, string columnName) =>
        string.IsNullOrWhiteSpace(schema)
            ? $"SELECT COUNT(*) INTO v_cnt FROM USER_TAB_COLUMNS WHERE TABLE_NAME = '{EscapeLiteral(tableName)}' AND COLUMN_NAME = '{EscapeLiteral(columnName)}';"
            : $"SELECT COUNT(*) INTO v_cnt FROM ALL_TAB_COLUMNS WHERE OWNER = '{EscapeLiteral(schema)}' AND TABLE_NAME = '{EscapeLiteral(tableName)}' AND COLUMN_NAME = '{EscapeLiteral(columnName)}';";

    private static string BuildConstraintExistsQuery(string? schema, string tableName, string constraintName) =>
        string.IsNullOrWhiteSpace(schema)
            ? $"SELECT COUNT(*) INTO v_cnt FROM USER_CONSTRAINTS WHERE TABLE_NAME = '{EscapeLiteral(tableName)}' AND CONSTRAINT_NAME = '{EscapeLiteral(constraintName)}';"
            : $"SELECT COUNT(*) INTO v_cnt FROM ALL_CONSTRAINTS WHERE OWNER = '{EscapeLiteral(schema)}' AND TABLE_NAME = '{EscapeLiteral(tableName)}' AND CONSTRAINT_NAME = '{EscapeLiteral(constraintName)}';";

    private static string BuildIndexExistsQuery(string? schema, string indexName) =>
        string.IsNullOrWhiteSpace(schema)
            ? $"SELECT COUNT(*) INTO v_cnt FROM USER_INDEXES WHERE INDEX_NAME = '{EscapeLiteral(indexName)}';"
            : $"SELECT COUNT(*) INTO v_cnt FROM ALL_INDEXES WHERE OWNER = '{EscapeLiteral(schema)}' AND INDEX_NAME = '{EscapeLiteral(indexName)}';";

    private static string BuildSequenceExistsQuery(string? schema, string sequenceName) =>
        string.IsNullOrWhiteSpace(schema)
            ? $"SELECT COUNT(*) INTO v_cnt FROM USER_SEQUENCES WHERE SEQUENCE_NAME = '{EscapeLiteral(sequenceName)}';"
            : $"SELECT COUNT(*) INTO v_cnt FROM ALL_SEQUENCES WHERE SEQUENCE_OWNER = '{EscapeLiteral(schema)}' AND SEQUENCE_NAME = '{EscapeLiteral(sequenceName)}';";

    private static void AppendCatalogGuardedDdl(
        StringBuilder sb,
        string catalogCountQuery,
        bool executeWhenExists,
        string statement)
    {
        var condition = executeWhenExists ? "v_cnt > 0" : "v_cnt = 0";
        sb.AppendLine("DECLARE");
        sb.AppendLine("    v_cnt NUMBER := 0;");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    {catalogCountQuery}");
        sb.AppendLine($"    IF {condition} THEN");
        sb.AppendLine($"        EXECUTE IMMEDIATE '{EscapeLiteral(statement)}';");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END;");
        sb.AppendLine("/");
    }

    private static string FormatColumnDefinition(ColumnSchema col)
    {
        if (!string.IsNullOrWhiteSpace(col.ComputedColumnSql))
        {
            return $"{Quote(col.ColumnName)} {col.SqlType} GENERATED ALWAYS AS ({col.ComputedColumnSql}) VIRTUAL";
        }

        var parts = new List<string> { Quote(col.ColumnName), col.SqlType };

        if (col.IsIdentity)
        {
            parts.Add("GENERATED BY DEFAULT ON NULL AS IDENTITY");
        }
        else if (!string.IsNullOrWhiteSpace(col.DefaultValueSql))
        {
            parts.Add($"DEFAULT {NormalizeOracleDefault(col.DefaultValueSql, col.SqlType)}");
        }

        parts.Add(col.IsNullable ? "NULL" : "NOT NULL");
        return string.Join(" ", parts);
    }

    private static string NormalizeOracleDefault(string defaultSql, string sqlType)
    {
        if (string.Equals(sqlType.Trim(), "NUMBER(1)", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = defaultSql.Trim();
            if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return "1";
            if (trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return "0";
        }

        return defaultSql;
    }

    private static string FormatForeignKeyClause(ForeignKeySchema fk)
    {
        var depCols = string.Join(", ", fk.DependentColumns.Select(Quote));
        var prinCols = string.Join(", ", fk.PrincipalColumns.Select(Quote));
        var onDelete = fk.OnDelete switch
        {
            DbReferentialAction.Cascade => " ON DELETE CASCADE",
            DbReferentialAction.SetNull => " ON DELETE SET NULL",
            _ => string.Empty
        };

        return $"CONSTRAINT {Quote(fk.Name)} FOREIGN KEY ({depCols}) " +
               $"REFERENCES {Qualify(fk.PrincipalSchema, fk.PrincipalTable)} ({prinCols}){onDelete}";
    }
}
