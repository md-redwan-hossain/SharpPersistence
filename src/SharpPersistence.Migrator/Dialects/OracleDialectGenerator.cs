using System.Globalization;
using System.Text;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Dialects;

internal sealed class OracleDialectGenerator : ISqlDialectGenerator
{
    private readonly OracleDbCompatibility _compatibility;

    public OracleDialectGenerator(OracleDbCompatibility compatibility = OracleDbCompatibility.Oracle19)
    {
        _compatibility = compatibility;
    }

    private bool SupportsIfExists => _compatibility >= OracleDbCompatibility.Oracle23;

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
                    or AddExclusionConstraintOp or DropExclusionConstraintOp
                    or SetTableLoggedOp or SetTableTablespaceOp or AlterTableStorageParametersOp
                    or SetIndexTablespaceOp or AlterIndexStorageParametersOp:
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
                    if (!string.Equals(
                            string.IsNullOrWhiteSpace(rt.OldSchema) ? string.Empty : rt.OldSchema.Trim(),
                            string.IsNullOrWhiteSpace(rt.NewSchema) ? string.Empty : rt.NewSchema.Trim(),
                            StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            $"Oracle does not support moving table '{rt.OldTableName}' across schemas ('{rt.OldSchema}' -> '{rt.NewSchema}') via ALTER TABLE RENAME.");
                    }

                    AppendCatalogGuardedRename(
                        sb,
                        BuildTableExistsQuery(rt.OldSchema, rt.OldTableName),
                        BuildTableExistsQuery(rt.NewSchema, rt.NewTableName),
                        $"ALTER TABLE {Qualify(rt.OldSchema, rt.OldTableName)} RENAME TO {Quote(rt.NewTableName)}");
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
                    AppendCatalogGuardedRename(
                        sb,
                        BuildIndexExistsQuery(ri.Schema, ri.OldName),
                        BuildIndexExistsQuery(ri.Schema, ri.NewName),
                        $"ALTER INDEX {Qualify(ri.Schema, ri.OldName)} RENAME TO {Quote(ri.NewName)}");
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
                    AppendCatalogGuardedDdl(
                        sb,
                        BuildConstraintExistsQuery(apk.Schema, apk.TableName, apk.PrimaryKey.Name),
                        executeWhenExists: false,
                        $"ALTER TABLE {Qualify(apk.Schema, apk.TableName)} ADD CONSTRAINT {Quote(apk.PrimaryKey.Name)} PRIMARY KEY ({pkCols})");
                    sb.AppendLine();
                    break;

                case DropCheckConstraintOp dck:
                    AppendDropConstraint(sb, dck.Schema, dck.TableName, dck.Name);
                    sb.AppendLine();
                    break;

                case AddCheckConstraintOp ack:
                    AppendCatalogGuardedDdl(
                        sb,
                        BuildConstraintExistsQuery(ack.Schema, ack.TableName, ack.Constraint.Name),
                        executeWhenExists: false,
                        $"ALTER TABLE {Qualify(ack.Schema, ack.TableName)} ADD CONSTRAINT {Quote(ack.Constraint.Name)} CHECK ({ack.Constraint.Sql})");
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
                    AppendCatalogGuardedRename(
                        sb,
                        BuildConstraintExistsQuery(rc.Schema, rc.TableName, rc.OldName),
                        BuildConstraintExistsQuery(rc.Schema, rc.TableName, rc.NewName),
                        $"ALTER TABLE {Qualify(rc.Schema, rc.TableName)} RENAME CONSTRAINT {Quote(rc.OldName)} TO {Quote(rc.NewName)}");
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
                    AppendCatalogGuardedRename(
                        sb,
                        BuildColumnExistsQuery(rnc.Schema, rnc.TableName, rnc.OldName),
                        BuildColumnExistsQuery(rnc.Schema, rnc.TableName, rnc.NewName),
                        $"ALTER TABLE {Qualify(rnc.Schema, rnc.TableName)} RENAME COLUMN {Quote(rnc.OldName)} TO {Quote(rnc.NewName)}");
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
        AppendCatalogGuardedDdl(
            sb,
            BuildColumnExistsQuery(ac.Schema, ac.TableName, ac.Column.ColumnName),
            executeWhenExists: false,
            $"ALTER TABLE {Qualify(ac.Schema, ac.TableName)} ADD ({colDef})");
    }

    private void AppendAlterColumn(StringBuilder sb, AlterColumnOp alc)
    {
        var table = Qualify(alc.Schema, alc.TableName);
        var col = Quote(alc.NewCol.ColumnName);

        if (!string.Equals(alc.OldCol.ComputedColumnSql, alc.NewCol.ComputedColumnSql, StringComparison.Ordinal))
        {
            if (SupportsIfExists)
            {
                sb.AppendLine($"ALTER TABLE {table} DROP COLUMN IF EXISTS {col};");
            }
            else
            {
                AppendCatalogGuardedDdl(
                    sb,
                    BuildColumnExistsQuery(alc.Schema, alc.TableName, alc.NewCol.ColumnName),
                    executeWhenExists: true,
                    $"ALTER TABLE {table} DROP COLUMN {col}");
            }

            AppendCatalogGuardedDdl(
                sb,
                BuildColumnExistsQuery(alc.Schema, alc.TableName, alc.NewCol.ColumnName),
                executeWhenExists: false,
                $"ALTER TABLE {table} ADD ({FormatColumnDefinition(alc.NewCol)})");

            return;
        }

        var dropIdentity = alc.OldCol.IsIdentity && !alc.NewCol.IsIdentity;
        var addIdentity = !alc.OldCol.IsIdentity && alc.NewCol.IsIdentity;
        var hadOldDefault = !string.IsNullOrWhiteSpace(alc.OldCol.DefaultValueSql);

        if (dropIdentity)
        {
            sb.AppendLine($"ALTER TABLE {table} MODIFY ({col} DROP IDENTITY);");
        }

        if (addIdentity && hadOldDefault)
        {
            sb.AppendLine($"ALTER TABLE {table} MODIFY ({col} DEFAULT NULL);");
        }

        if (addIdentity)
        {
            sb.AppendLine($"ALTER TABLE {table} MODIFY ({col} GENERATED BY DEFAULT ON NULL AS IDENTITY);");
        }

        var parts = new List<string> { col };

        if (!string.Equals(alc.OldCol.SqlType, alc.NewCol.SqlType, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(alc.NewCol.SqlType);
        }

        if (!addIdentity && !string.Equals(alc.OldCol.DefaultValueSql, alc.NewCol.DefaultValueSql, StringComparison.Ordinal))
        {
            parts.Add(string.IsNullOrWhiteSpace(alc.NewCol.DefaultValueSql)
                ? "DEFAULT NULL"
                : $"DEFAULT {NormalizeOracleDefault(alc.NewCol.DefaultValueSql, alc.NewCol.SqlType)}");
        }

        if (parts.Count > 1)
        {
            sb.AppendLine($"ALTER TABLE {table} MODIFY ({string.Join(" ", parts)});");
        }

        if (alc.OldCol.IsNullable != alc.NewCol.IsNullable)
        {
            var expectedCurrentNullable = alc.NewCol.IsNullable ? "N" : "Y";
            var nullClause = alc.NewCol.IsNullable ? "NULL" : "NOT NULL";
            AppendCatalogGuardedDdl(
                sb,
                BuildColumnNullabilityQuery(alc.Schema, alc.TableName, alc.NewCol.ColumnName, expectedCurrentNullable),
                executeWhenExists: true,
                $"ALTER TABLE {table} MODIFY ({col} {nullClause})");
        }
    }

    private void AppendAddForeignKey(StringBuilder sb, AddForeignKeyOp afk)
    {
        var clause = FormatForeignKeyClause(afk.ForeignKey);
        AppendCatalogGuardedDdl(
            sb,
            BuildConstraintExistsQuery(afk.Schema, afk.TableName, afk.ForeignKey.Name),
            executeWhenExists: false,
            $"ALTER TABLE {Qualify(afk.Schema, afk.TableName)} ADD {clause}");
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

    private static void AppendDropConstraint(StringBuilder sb, string? schema, string tableName, string constraintName)
    {
        AppendCatalogGuardedDdl(
            sb,
            BuildConstraintExistsQuery(schema, tableName, constraintName),
            executeWhenExists: true,
            $"ALTER TABLE {Qualify(schema, tableName)} DROP CONSTRAINT {Quote(constraintName)}");
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

    private static string BuildColumnNullabilityQuery(string? schema, string tableName, string columnName, string expectedNullable) =>
        string.IsNullOrWhiteSpace(schema)
            ? $"SELECT COUNT(*) INTO v_cnt FROM USER_TAB_COLUMNS WHERE TABLE_NAME = '{EscapeLiteral(tableName)}' AND COLUMN_NAME = '{EscapeLiteral(columnName)}' AND NULLABLE = '{expectedNullable}';"
            : $"SELECT COUNT(*) INTO v_cnt FROM ALL_TAB_COLUMNS WHERE OWNER = '{EscapeLiteral(schema)}' AND TABLE_NAME = '{EscapeLiteral(tableName)}' AND COLUMN_NAME = '{EscapeLiteral(columnName)}' AND NULLABLE = '{expectedNullable}';";

    private static void AppendCatalogGuardedRename(
        StringBuilder sb,
        string oldExistsQuery,
        string newExistsQuery,
        string statement)
    {
        var oldQuery = oldExistsQuery.Replace("INTO v_cnt", "INTO v_old", StringComparison.Ordinal);
        var newQuery = newExistsQuery.Replace("INTO v_cnt", "INTO v_new", StringComparison.Ordinal);
        sb.AppendLine("DECLARE");
        sb.AppendLine("    v_old NUMBER := 0;");
        sb.AppendLine("    v_new NUMBER := 0;");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    {oldQuery}");
        sb.AppendLine($"    {newQuery}");
        sb.AppendLine("    IF v_old > 0 AND v_new = 0 THEN");
        sb.AppendLine($"        EXECUTE IMMEDIATE '{EscapeLiteral(statement)}';");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END;");
        sb.AppendLine("/");
    }

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
            DbReferentialAction.SetDefault => throw new NotSupportedException(
                $"Oracle does not support ON DELETE SET DEFAULT on foreign key '{fk.Name}'."),
            _ => string.Empty
        };

        return $"CONSTRAINT {Quote(fk.Name)} FOREIGN KEY ({depCols}) " +
               $"REFERENCES {Qualify(fk.PrincipalSchema, fk.PrincipalTable)} ({prinCols}){onDelete}";
    }
}
