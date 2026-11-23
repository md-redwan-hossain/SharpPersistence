using System.Text;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Dialects;

public sealed class PostgresDialectGenerator : ISqlDialectGenerator
{
    public string GenerateSql(IReadOnlyList<SchemaOperation> ops)
    {
        if (ops.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN;");
        sb.AppendLine();

        foreach (var op in ops)
        {
            switch (op)
            {
                case EnsureExtensionOp ext:
                    var extSchema = string.IsNullOrWhiteSpace(ext.Schema)
                        ? string.Empty
                        : $" WITH SCHEMA {Quote(ext.Schema)}";
                    sb.AppendLine($"CREATE EXTENSION IF NOT EXISTS {Quote(ext.Name)}{extSchema};");
                    sb.AppendLine();
                    break;

                case DropExtensionOp dext:
                    sb.AppendLine($"DROP EXTENSION IF EXISTS {Quote(dext.Name)};");
                    sb.AppendLine();
                    break;

                case CreateEnumOp ce:
                    AppendCreateEnum(sb, ce.Enum);
                    sb.AppendLine();
                    break;

                case AddEnumValueOp aev:
                    sb.AppendLine(
                        $"ALTER TYPE {Qualify(aev.Schema, aev.EnumName)} ADD VALUE IF NOT EXISTS '{EscapeLiteral(aev.Value)}';");
                    sb.AppendLine();
                    break;

                case DropEnumOp de:
                    sb.AppendLine($"DROP TYPE IF EXISTS {Qualify(de.Schema, de.Name)};");
                    sb.AppendLine();
                    break;

                case EnsureSchemaOp s:
                    sb.AppendLine($"CREATE SCHEMA IF NOT EXISTS {Quote(s.SchemaName)};");
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
                    sb.AppendLine($"DROP SEQUENCE IF EXISTS {Qualify(ds.Schema, ds.Name)};");
                    sb.AppendLine();
                    break;

                case CreateTableOp c:
                    AppendCreateTable(sb, c.Table);
                    sb.AppendLine();
                    break;

                case DropTableOp d:
                    sb.AppendLine($"DROP TABLE IF EXISTS {Qualify(d.Schema, d.TableName)} CASCADE;");
                    sb.AppendLine();
                    break;

                case RenameTableOp rt:
                    AppendRenameTable(sb, rt);
                    sb.AppendLine();
                    break;

                case DropIndexOp di:
                    var qualifiedDropIx = string.IsNullOrWhiteSpace(di.Schema)
                        ? Quote(di.Name)
                        : $"{Quote(di.Schema)}.{Quote(di.Name)}";
                    sb.AppendLine($"DROP INDEX IF EXISTS {qualifiedDropIx};");
                    sb.AppendLine();
                    break;

                case RenameIndexOp ri:
                    AppendRenameIndex(sb, ri);
                    sb.AppendLine();
                    break;

                case CreateIndexOp ci:
                    var unique = ci.Index.IsUnique ? "UNIQUE " : string.Empty;
                    var usingClause = string.IsNullOrWhiteSpace(ci.Index.Method)
                        ? string.Empty
                        : $" USING {ci.Index.Method}";
                    var cols = string.Join(", ", ci.Index.ColumnNames.Select((c, idx) =>
                    {
                        var colExpr = Quote(c);
                        if (ci.Index.OperatorsByColumn is not null &&
                            ci.Index.OperatorsByColumn.TryGetValue(c, out var opClass) &&
                            !string.IsNullOrWhiteSpace(opClass))
                        {
                            colExpr = $"{colExpr} {opClass}";
                        }

                        var isDesc = ci.Index.IsDescending is not null &&
                                     (ci.Index.IsDescending.Count == 1
                                         ? ci.Index.IsDescending[0]
                                         : idx < ci.Index.IsDescending.Count && ci.Index.IsDescending[idx]);
                        return isDesc ? $"{colExpr} DESC" : colExpr;
                    }));
                    var includeClause = ci.Index.IncludedColumnNames is { Count: > 0 }
                        ? $" INCLUDE ({string.Join(", ", ci.Index.IncludedColumnNames.Select(Quote))})"
                        : string.Empty;
                    var indexWithClause = ci.Index.StorageParameters is { Count: > 0 }
                        ? $" WITH ({string.Join(", ", ci.Index.StorageParameters.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key} = {kv.Value}"))})"
                        : string.Empty;
                    var indexTablespace = string.IsNullOrWhiteSpace(ci.Index.Tablespace)
                        ? string.Empty
                        : $" TABLESPACE {Quote(ci.Index.Tablespace)}";
                    var filter = string.IsNullOrWhiteSpace(ci.Index.FilterSql)
                        ? string.Empty
                        : $" WHERE {ci.Index.FilterSql}";
                    sb.AppendLine(
                        $"CREATE {unique}INDEX IF NOT EXISTS {Quote(ci.Index.Name)} ON {Qualify(ci.Schema, ci.TableName)}{usingClause} ({cols}){includeClause}{indexWithClause}{indexTablespace}{filter};");
                    sb.AppendLine();
                    break;

                case SetTableCommentOp stc:
                    var tableComment = stc.Comment is null ? "NULL" : $"'{EscapeLiteral(stc.Comment)}'";
                    sb.AppendLine($"COMMENT ON TABLE {Qualify(stc.Schema, stc.TableName)} IS {tableComment};");
                    sb.AppendLine();
                    break;

                case SetColumnCommentOp scc:
                    var colComment = scc.Comment is null ? "NULL" : $"'{EscapeLiteral(scc.Comment)}'";
                    sb.AppendLine(
                        $"COMMENT ON COLUMN {Qualify(scc.Schema, scc.TableName)}.{Quote(scc.ColumnName)} IS {colComment};");
                    sb.AppendLine();
                    break;

                case SetTableLoggedOp stl:
                    var loggedClause = stl.IsUnlogged ? "SET UNLOGGED" : "SET LOGGED";
                    sb.AppendLine($"ALTER TABLE IF EXISTS {Qualify(stl.Schema, stl.TableName)} {loggedClause};");
                    sb.AppendLine();
                    break;

                case SetTableTablespaceOp stt:
                    sb.AppendLine($"ALTER TABLE IF EXISTS {Qualify(stt.Schema, stt.TableName)} SET TABLESPACE {Quote(stt.Tablespace)};");
                    sb.AppendLine();
                    break;

                case AlterTableStorageParametersOp asp:
                    if (asp.SetParameters.Count > 0)
                    {
                        var setClause = string.Join(", ", asp.SetParameters
                            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(kv => $"{kv.Key} = {kv.Value}"));
                        sb.AppendLine($"ALTER TABLE IF EXISTS {Qualify(asp.Schema, asp.TableName)} SET ({setClause});");
                    }
                    if (asp.ResetParameters.Count > 0)
                    {
                        var resetClause = string.Join(", ", asp.ResetParameters);
                        sb.AppendLine($"ALTER TABLE IF EXISTS {Qualify(asp.Schema, asp.TableName)} RESET ({resetClause});");
                    }
                    sb.AppendLine();
                    break;

                case SetIndexTablespaceOp sit:
                    var qualifiedIx = string.IsNullOrWhiteSpace(sit.Schema)
                        ? Quote(sit.IndexName)
                        : $"{Quote(sit.Schema)}.{Quote(sit.IndexName)}";
                    sb.AppendLine($"ALTER INDEX IF EXISTS {qualifiedIx} SET TABLESPACE {Quote(sit.Tablespace)};");
                    sb.AppendLine();
                    break;

                case AlterIndexStorageParametersOp aisp:
                    var qualifiedParamIx = string.IsNullOrWhiteSpace(aisp.Schema)
                        ? Quote(aisp.IndexName)
                        : $"{Quote(aisp.Schema)}.{Quote(aisp.IndexName)}";
                    if (aisp.SetParameters.Count > 0)
                    {
                        var setClause = string.Join(", ", aisp.SetParameters
                            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(kv => $"{kv.Key} = {kv.Value}"));
                        sb.AppendLine($"ALTER INDEX IF EXISTS {qualifiedParamIx} SET ({setClause});");
                    }
                    if (aisp.ResetParameters.Count > 0)
                    {
                        var resetClause = string.Join(", ", aisp.ResetParameters);
                        sb.AppendLine($"ALTER INDEX IF EXISTS {qualifiedParamIx} RESET ({resetClause});");
                    }
                    sb.AppendLine();
                    break;

                case DropPrimaryKeyOp dpk:
                    sb.AppendLine(
                        $"ALTER TABLE IF EXISTS {Qualify(dpk.Schema, dpk.TableName)} DROP CONSTRAINT IF EXISTS {Quote(dpk.Name)};");
                    sb.AppendLine();
                    break;

                case AddPrimaryKeyOp apk:
                    var pkCols = string.Join(", ", apk.PrimaryKey.ColumnNames.Select(Quote));
                    AppendAddConstraintIdempotent(
                        sb,
                        apk.Schema,
                        apk.TableName,
                        apk.PrimaryKey.Name,
                        $"CONSTRAINT {Quote(apk.PrimaryKey.Name)} PRIMARY KEY ({pkCols})");
                    sb.AppendLine();
                    break;

                case DropCheckConstraintOp dck:
                    sb.AppendLine(
                        $"ALTER TABLE IF EXISTS {Qualify(dck.Schema, dck.TableName)} DROP CONSTRAINT IF EXISTS {Quote(dck.Name)};");
                    sb.AppendLine();
                    break;

                case AddCheckConstraintOp ack:
                    AppendAddConstraintIdempotent(
                        sb,
                        ack.Schema,
                        ack.TableName,
                        ack.Constraint.Name,
                        $"CONSTRAINT {Quote(ack.Constraint.Name)} CHECK ({ack.Constraint.Sql})");
                    sb.AppendLine();
                    break;

                case DropForeignKeyOp dfk:
                    sb.AppendLine(
                        $"ALTER TABLE IF EXISTS {Qualify(dfk.Schema, dfk.TableName)} DROP CONSTRAINT IF EXISTS {Quote(dfk.Name)};");
                    sb.AppendLine();
                    break;

                case AddForeignKeyOp afk:
                    AppendAddConstraintIdempotent(
                        sb,
                        afk.Schema,
                        afk.TableName,
                        afk.ForeignKey.Name,
                        FormatForeignKeyConstraint(afk.ForeignKey));
                    sb.AppendLine();
                    break;

                case RenameConstraintOp rcon:
                    AppendRenameConstraint(sb, rcon);
                    sb.AppendLine();
                    break;

                case RenameColumnOp rc:
                    AppendRenameColumn(sb, rc);
                    sb.AppendLine();
                    break;

                case DropColumnOp dc:
                    sb.AppendLine(
                        $"ALTER TABLE IF EXISTS {Qualify(dc.Schema, dc.TableName)} DROP COLUMN IF EXISTS {Quote(dc.ColumnName)};");
                    sb.AppendLine();
                    break;

                case AddColumnOp ac:
                    sb.AppendLine(
                        $"ALTER TABLE IF EXISTS {Qualify(ac.Schema, ac.TableName)} ADD COLUMN IF NOT EXISTS {FormatColumnDef(ac.Column)};");
                    sb.AppendLine();
                    break;

                case AlterColumnOp alt:
                    AppendAlterColumn(sb, alt);
                    sb.AppendLine();
                    break;
            }
        }

        sb.AppendLine("COMMIT;");
        return sb.ToString().TrimEnd();
    }

    private static void AppendCreateEnum(StringBuilder sb, PostgresEnumSchema pgEnum)
    {
        var ns = EscapeLiteral(string.IsNullOrWhiteSpace(pgEnum.Schema) ? "public" : pgEnum.Schema);
        var typ = EscapeLiteral(pgEnum.Name);
        var labels = string.Join(", ", pgEnum.Labels.Select(l => $"'{EscapeLiteral(l)}'"));

        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF NOT EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_type t");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace");
        sb.AppendLine($"        WHERE n.nspname = '{ns}' AND t.typname = '{typ}'");
        sb.AppendLine("    ) THEN");
        sb.AppendLine($"        CREATE TYPE {Qualify(pgEnum.Schema, pgEnum.Name)} AS ENUM ({labels});");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    private static void AppendCreateSequence(StringBuilder sb, SequenceSchema seq)
    {
        var parts = new List<string>
        {
            $"CREATE SEQUENCE IF NOT EXISTS {Qualify(seq.Schema, seq.Name)}",
            $"AS {seq.SqlType}",
            $"INCREMENT BY {seq.IncrementBy}"
        };

        if (seq.MinValue.HasValue)
        {
            parts.Add($"MINVALUE {seq.MinValue.Value}");
        }

        if (seq.MaxValue.HasValue)
        {
            parts.Add($"MAXVALUE {seq.MaxValue.Value}");
        }

        parts.Add($"START WITH {seq.StartValue}");
        parts.Add(seq.IsCyclic ? "CYCLE" : "NO CYCLE");

        sb.AppendLine($"{string.Join(" ", parts)};");
    }

    private static void AppendAlterSequence(StringBuilder sb, AlterSequenceOp alt)
    {
        var s = alt.NewSeq;
        var parts = new List<string>
        {
            $"ALTER SEQUENCE IF EXISTS {Qualify(s.Schema, s.Name)}",
            $"INCREMENT BY {s.IncrementBy}",
            s.MinValue.HasValue ? $"MINVALUE {s.MinValue.Value}" : "NO MINVALUE",
            s.MaxValue.HasValue ? $"MAXVALUE {s.MaxValue.Value}" : "NO MAXVALUE",
            s.IsCyclic ? "CYCLE" : "NO CYCLE"
        };

        sb.AppendLine($"{string.Join(" ", parts)};");
    }

    private static void AppendCreateTable(StringBuilder sb, TableSchema table)
    {
        var unloggedClause = table.IsUnlogged ? "UNLOGGED " : string.Empty;
        sb.AppendLine($"CREATE {unloggedClause}TABLE IF NOT EXISTS {Qualify(table.Schema, table.TableName)} (");
        var lines = new List<string>();

        foreach (var col in table.Columns.Where(c => !c.IsSystemColumn))
        {
            lines.Add($"    {FormatColumnDef(col)}");
        }

        if (table.PrimaryKey is not null && table.PrimaryKey.ColumnNames.Count > 0)
        {
            var pkCols = string.Join(", ", table.PrimaryKey.ColumnNames.Select(Quote));
            lines.Add($"    CONSTRAINT {Quote(table.PrimaryKey.Name)} PRIMARY KEY ({pkCols})");
        }

        sb.AppendLine(string.Join("," + Environment.NewLine, lines));

        var hasWith = table.StorageParameters is { Count: > 0 };
        var hasTablespace = !string.IsNullOrWhiteSpace(table.Tablespace);

        if (!hasWith && !hasTablespace)
        {
            sb.AppendLine(");");
            return;
        }

        sb.AppendLine(")");
        if (hasWith)
        {
            sb.AppendLine("WITH (");
            var paramLines = table.StorageParameters!
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"    {kv.Key} = {kv.Value}")
                .ToList();
            sb.AppendLine(string.Join("," + Environment.NewLine, paramLines));
            sb.AppendLine(hasTablespace ? ")" : ");");
        }

        if (hasTablespace)
        {
            sb.AppendLine($"TABLESPACE {Quote(table.Tablespace!)};");
        }
    }

    private static void AppendAddConstraintIdempotent(
        StringBuilder sb,
        string? schema,
        string tableName,
        string constraintName,
        string constraintDefinitionSql)
    {
        var ns = EscapeLiteral(string.IsNullOrWhiteSpace(schema) ? "public" : schema);
        var rel = EscapeLiteral(tableName);
        var con = EscapeLiteral(constraintName);

        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF NOT EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_constraint c");
        sb.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = c.conrelid");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
        sb.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND c.conname = '{con}'");
        sb.AppendLine("    ) THEN");
        sb.AppendLine($"        ALTER TABLE {Qualify(schema, tableName)} ADD {constraintDefinitionSql};");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    private static void AppendRenameTable(StringBuilder sb, RenameTableOp rt)
    {
        var oldNs = string.IsNullOrWhiteSpace(rt.OldSchema) ? "public" : rt.OldSchema;
        var newNs = string.IsNullOrWhiteSpace(rt.NewSchema) ? "public" : rt.NewSchema;
        var currentSchema = rt.OldSchema;

        if (!string.Equals(oldNs, newNs, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(rt.NewSchema))
            {
                sb.AppendLine($"CREATE SCHEMA IF NOT EXISTS {Quote(rt.NewSchema)};");
            }

            sb.AppendLine("DO $$");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    IF EXISTS (");
            sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_class t");
            sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            sb.AppendLine(
                $"        WHERE n.nspname = '{EscapeLiteral(oldNs)}' AND t.relname = '{EscapeLiteral(rt.OldTableName)}' AND t.relkind = 'r'");
            sb.AppendLine("    ) AND NOT EXISTS (");
            sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_class t");
            sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            sb.AppendLine(
                $"        WHERE n.nspname = '{EscapeLiteral(newNs)}' AND t.relname = '{EscapeLiteral(rt.OldTableName)}' AND t.relkind = 'r'");
            sb.AppendLine("    ) THEN");
            sb.AppendLine($"        ALTER TABLE {Qualify(rt.OldSchema, rt.OldTableName)} SET SCHEMA {Quote(newNs)};");
            sb.AppendLine("    END IF;");
            sb.AppendLine("END $$;");

            currentSchema = rt.NewSchema;
        }

        if (!string.Equals(rt.OldTableName, rt.NewTableName, StringComparison.Ordinal))
        {
            var targetNs = string.IsNullOrWhiteSpace(currentSchema) ? "public" : currentSchema;
            sb.AppendLine("DO $$");
            sb.AppendLine("BEGIN");
            sb.AppendLine("    IF EXISTS (");
            sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_class t");
            sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            sb.AppendLine(
                $"        WHERE n.nspname = '{EscapeLiteral(targetNs)}' AND t.relname = '{EscapeLiteral(rt.OldTableName)}' AND t.relkind = 'r'");
            sb.AppendLine("    ) AND NOT EXISTS (");
            sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_class t");
            sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            sb.AppendLine(
                $"        WHERE n.nspname = '{EscapeLiteral(targetNs)}' AND t.relname = '{EscapeLiteral(rt.NewTableName)}' AND t.relkind = 'r'");
            sb.AppendLine("    ) THEN");
            sb.AppendLine(
                $"        ALTER TABLE {Qualify(currentSchema, rt.OldTableName)} RENAME TO {Quote(rt.NewTableName)};");
            sb.AppendLine("    END IF;");
            sb.AppendLine("END $$;");
        }
    }

    private static void AppendRenameConstraint(StringBuilder sb, RenameConstraintOp rc)
    {
        var ns = EscapeLiteral(string.IsNullOrWhiteSpace(rc.Schema) ? "public" : rc.Schema);
        var rel = EscapeLiteral(rc.TableName);
        var oldCon = EscapeLiteral(rc.OldName);
        var newCon = EscapeLiteral(rc.NewName);

        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_constraint c");
        sb.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = c.conrelid");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
        sb.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND c.conname = '{oldCon}'");
        sb.AppendLine("    ) AND NOT EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_constraint c");
        sb.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = c.conrelid");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
        sb.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND c.conname = '{newCon}'");
        sb.AppendLine("    ) THEN");
        sb.AppendLine(
            $"        ALTER TABLE {Qualify(rc.Schema, rc.TableName)} RENAME CONSTRAINT {Quote(rc.OldName)} TO {Quote(rc.NewName)};");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    private static void AppendRenameColumn(StringBuilder sb, RenameColumnOp rc)
    {
        var ns = EscapeLiteral(string.IsNullOrWhiteSpace(rc.Schema) ? "public" : rc.Schema);
        var rel = EscapeLiteral(rc.TableName);
        var oldCol = EscapeLiteral(rc.OldName);
        var newCol = EscapeLiteral(rc.NewName);

        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_attribute a");
        sb.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
        sb.AppendLine(
            $"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{oldCol}' AND NOT a.attisdropped");
        sb.AppendLine("    ) AND NOT EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_attribute a");
        sb.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
        sb.AppendLine(
            $"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{newCol}' AND NOT a.attisdropped");
        sb.AppendLine("    ) THEN");
        sb.AppendLine(
            $"        ALTER TABLE {Qualify(rc.Schema, rc.TableName)} RENAME COLUMN {Quote(rc.OldName)} TO {Quote(rc.NewName)};");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    private static void AppendRenameIndex(StringBuilder sb, RenameIndexOp ri)
    {
        var ns = EscapeLiteral(string.IsNullOrWhiteSpace(ri.Schema) ? "public" : ri.Schema);
        var oldIx = EscapeLiteral(ri.OldName);
        var newIx = EscapeLiteral(ri.NewName);
        var qualifiedOld = string.IsNullOrWhiteSpace(ri.Schema)
            ? Quote(ri.OldName)
            : $"{Quote(ri.Schema)}.{Quote(ri.OldName)}";

        sb.AppendLine("DO $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    IF EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_class i");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = i.relnamespace");
        sb.AppendLine($"        WHERE n.nspname = '{ns}' AND i.relname = '{oldIx}' AND i.relkind = 'i'");
        sb.AppendLine("    ) AND NOT EXISTS (");
        sb.AppendLine("        SELECT 1 FROM pg_catalog.pg_class i");
        sb.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = i.relnamespace");
        sb.AppendLine($"        WHERE n.nspname = '{ns}' AND i.relname = '{newIx}' AND i.relkind = 'i'");
        sb.AppendLine("    ) THEN");
        sb.AppendLine($"        ALTER INDEX {qualifiedOld} RENAME TO {Quote(ri.NewName)};");
        sb.AppendLine("    END IF;");
        sb.AppendLine("END $$;");
    }

    private static void AppendAlterColumn(StringBuilder sb, AlterColumnOp alt)
    {
        var ns = EscapeLiteral(string.IsNullOrWhiteSpace(alt.Schema) ? "public" : alt.Schema);
        var rel = EscapeLiteral(alt.TableName);
        var colName = EscapeLiteral(alt.NewCol.ColumnName);
        var table = Qualify(alt.Schema, alt.TableName);
        var col = Quote(alt.NewCol.ColumnName);

        var computedChanged = !string.Equals(alt.OldCol.ComputedColumnSql, alt.NewCol.ComputedColumnSql, StringComparison.Ordinal)
                              || alt.OldCol.IsComputedStored != alt.NewCol.IsComputedStored;

        if (computedChanged && !string.IsNullOrWhiteSpace(alt.NewCol.ComputedColumnSql))
        {
            sb.AppendLine($"ALTER TABLE IF EXISTS {table} DROP COLUMN IF EXISTS {col};");
            sb.AppendLine($"ALTER TABLE IF EXISTS {table} ADD COLUMN IF NOT EXISTS {FormatColumnDef(alt.NewCol)};");
            return;
        }

        var inner = new StringBuilder();

        if (computedChanged && !string.IsNullOrWhiteSpace(alt.OldCol.ComputedColumnSql) &&
            string.IsNullOrWhiteSpace(alt.NewCol.ComputedColumnSql))
        {
            inner.AppendLine("    IF EXISTS (");
            inner.AppendLine("        SELECT 1 FROM pg_catalog.pg_attribute a");
            inner.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
            inner.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            inner.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{colName}'");
            inner.AppendLine("          AND NOT a.attisdropped AND a.attgenerated <> ''");
            inner.AppendLine("    ) THEN");
            inner.AppendLine($"        ALTER TABLE {table} ALTER COLUMN {col} DROP EXPRESSION;");
            inner.AppendLine("    END IF;");
        }

        if (!string.Equals(alt.OldCol.SqlType, alt.NewCol.SqlType, StringComparison.OrdinalIgnoreCase))
        {
            var targetType = EscapeLiteral(NormalizePostgresType(alt.NewCol.SqlType));
            inner.AppendLine("    IF EXISTS (");
            inner.AppendLine("        SELECT 1 FROM pg_catalog.pg_attribute a");
            inner.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
            inner.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            inner.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{colName}'");
            inner.AppendLine(
                $"          AND NOT a.attisdropped AND pg_catalog.format_type(a.atttypid, a.atttypmod) <> '{targetType}'");
            inner.AppendLine("    ) THEN");
            inner.AppendLine($"        ALTER TABLE {table} ALTER COLUMN {col} TYPE {alt.NewCol.SqlType};");
            inner.AppendLine("    END IF;");
        }

        if (alt.OldCol.IsIdentity != alt.NewCol.IsIdentity)
        {
            if (alt.NewCol.IsIdentity)
            {
                inner.AppendLine("    IF EXISTS (");
                inner.AppendLine("        SELECT 1 FROM pg_catalog.pg_attribute a");
                inner.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
                inner.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
                inner.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{colName}'");
                inner.AppendLine("          AND NOT a.attisdropped AND a.attidentity = ''");
                inner.AppendLine("    ) THEN");
                inner.AppendLine($"        ALTER TABLE {table} ALTER COLUMN {col} ADD GENERATED BY DEFAULT AS IDENTITY;");
                inner.AppendLine("    END IF;");
            }
            else
            {
                inner.AppendLine($"    ALTER TABLE {table} ALTER COLUMN {col} DROP IDENTITY IF EXISTS;");
            }
        }

        if (alt.OldCol.IsNullable != alt.NewCol.IsNullable)
        {
            var expectedNotNull = alt.NewCol.IsNullable ? "TRUE" : "FALSE";
            var setOrDrop = alt.NewCol.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
            inner.AppendLine("    IF EXISTS (");
            inner.AppendLine("        SELECT 1 FROM pg_catalog.pg_attribute a");
            inner.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
            inner.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
            inner.AppendLine($"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{colName}'");
            inner.AppendLine($"          AND NOT a.attisdropped AND a.attnotnull = {expectedNotNull}");
            inner.AppendLine("    ) THEN");
            inner.AppendLine($"        ALTER TABLE {table} ALTER COLUMN {col} {setOrDrop};");
            inner.AppendLine("    END IF;");
        }

        if (!string.Equals(alt.OldCol.DefaultValueSql, alt.NewCol.DefaultValueSql, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(alt.NewCol.DefaultValueSql))
            {
                inner.AppendLine("    IF EXISTS (");
                inner.AppendLine("        SELECT 1 FROM pg_catalog.pg_attrdef d");
                inner.AppendLine(
                    "        JOIN pg_catalog.pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum");
                inner.AppendLine("        JOIN pg_catalog.pg_class t ON t.oid = a.attrelid");
                inner.AppendLine("        JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace");
                inner.AppendLine(
                    $"        WHERE n.nspname = '{ns}' AND t.relname = '{rel}' AND a.attname = '{colName}' AND NOT a.attisdropped");
                inner.AppendLine("    ) THEN");
                inner.AppendLine($"        ALTER TABLE {table} ALTER COLUMN {col} DROP DEFAULT;");
                inner.AppendLine("    END IF;");
            }
            else
            {
                inner.AppendLine(
                    $"    ALTER TABLE {table} ALTER COLUMN {col} SET DEFAULT ({alt.NewCol.DefaultValueSql});");
            }
        }

        if (inner.Length > 0)
        {
            sb.AppendLine("DO $$");
            sb.AppendLine("BEGIN");
            sb.Append(inner);
            sb.AppendLine("END $$;");
        }
    }

    private static string FormatColumnDef(ColumnSchema col)
    {
        var nullClause = col.IsNullable ? "NULL" : "NOT NULL";

        if (!string.IsNullOrWhiteSpace(col.ComputedColumnSql))
        {
            return $"{Quote(col.ColumnName)} {col.SqlType} GENERATED ALWAYS AS ({col.ComputedColumnSql}) STORED {nullClause}";
        }

        var defaultClause = string.IsNullOrWhiteSpace(col.DefaultValueSql)
            ? string.Empty
            : $" DEFAULT ({col.DefaultValueSql})";
        var identityClause = col.IsIdentity ? " GENERATED BY DEFAULT AS IDENTITY" : string.Empty;

        return $"{Quote(col.ColumnName)} {col.SqlType} {nullClause}{defaultClause}{identityClause}";
    }

    private static string FormatForeignKeyConstraint(ForeignKeySchema fk)
    {
        var depCols = string.Join(", ", fk.DependentColumns.Select(Quote));
        var prinCols = string.Join(", ", fk.PrincipalColumns.Select(Quote));
        var onDelete = fk.OnDelete switch
        {
            DbReferentialAction.Cascade => " ON DELETE CASCADE",
            DbReferentialAction.SetNull => " ON DELETE SET NULL",
            DbReferentialAction.SetDefault => " ON DELETE SET DEFAULT",
            DbReferentialAction.Restrict => " ON DELETE RESTRICT",
            _ => string.Empty
        };

        return
            $"CONSTRAINT {Quote(fk.Name)} FOREIGN KEY ({depCols}) REFERENCES {Qualify(fk.PrincipalSchema, fk.PrincipalTable)} ({prinCols}){onDelete}";
    }

    private static string NormalizePostgresType(string sqlType)
    {
        var lower = sqlType.Trim().ToLowerInvariant();
        if (lower.StartsWith("varchar(", StringComparison.Ordinal))
        {
            return "character varying" + lower["varchar".Length..];
        }

        if (lower.StartsWith("decimal(", StringComparison.Ordinal))
        {
            return "numeric" + lower["decimal".Length..];
        }

        return lower switch
        {
            "int" or "int4" => "integer",
            "int8" => "bigint",
            "int2" => "smallint",
            "bool" => "boolean",
            "float8" => "double precision",
            "float4" => "real",
            "timestamptz" => "timestamp with time zone",
            "timestamp" => "timestamp without time zone",
            "varchar" => "character varying",
            "decimal" => "numeric",
            _ => lower
        };
    }
}
