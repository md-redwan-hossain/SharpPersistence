using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.EfCore;

internal sealed record TranslatedEfCoreMigration(string MigrationId, string SqlScript);

internal static partial class EfCoreMigrationTranslator
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    [GeneratedRegex(@"\[Migration\(""([^""]+)""\)\]", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationAttrRegex();

    public static IReadOnlyList<TranslatedEfCoreMigration> TranslateFromDirectory(
        string efCoreMigrationsDirectory,
        SqlDialect sqlDialect = SqlDialect.PostgreSql,
        OracleDbCompatibility oracleCompatibility = OracleDbCompatibility.OracleDb19,
        IEnumerable<Assembly>? searchAssemblies = null)
    {
        if (string.IsNullOrWhiteSpace(efCoreMigrationsDirectory))
        {
            throw new ArgumentException("EF Core migrations directory path cannot be empty.", nameof(efCoreMigrationsDirectory));
        }

        if (!Directory.Exists(efCoreMigrationsDirectory))
        {
            throw new DirectoryNotFoundException($"EF Core migrations directory not found: '{efCoreMigrationsDirectory}'.");
        }

        var folderMigrationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var csFile in Directory.EnumerateFiles(efCoreMigrationsDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(csFile);
            foreach (Match match in MigrationAttrRegex().Matches(text))
            {
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    folderMigrationIds.Add(match.Groups[1].Value);
                }
            }
        }

        if (folderMigrationIds.Count == 0)
        {
            return [];
        }

        var assemblies = (searchAssemblies ?? AppDomain.CurrentDomain.GetAssemblies()).Distinct().ToList();
        var compiledById = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(Migration).IsAssignableFrom(type))
                {
                    continue;
                }

                var attr = type.GetCustomAttribute<MigrationAttribute>();
                if (attr is not null && folderMigrationIds.Contains(attr.Id))
                {
                    compiledById[attr.Id] = type;
                }
            }
        }

        var missing = folderMigrationIds.Where(id => !compiledById.ContainsKey(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Found {missing.Count} EF Core migration(s) in '{efCoreMigrationsDirectory}' that are not compiled/loaded in the current process: " +
                $"{string.Join(", ", missing)}. Ensure the project containing these migrations is built and referenced.");
        }

        ISqlDialectGenerator dialect = sqlDialect switch
        {
            SqlDialect.PostgreSql => new PostgresDialectGenerator(),
            SqlDialect.Oracle => new OracleDialectGenerator(oracleCompatibility),
            _ => throw new NotSupportedException($"SQL dialect '{sqlDialect}' is not supported for EF Core translation.")
        };
        var results = new List<TranslatedEfCoreMigration>(compiledById.Count);

        foreach (var (migrationId, migrationType) in compiledById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var migration = (Migration?)Activator.CreateInstance(migrationType)
                            ?? throw new InvalidOperationException($"Could not instantiate EF Core migration '{migrationType.FullName}'.");

            var providerName = sqlDialect == SqlDialect.Oracle
                ? "Oracle.EntityFrameworkCore"
                : NpgsqlProviderName;
            var builder = new MigrationBuilder(providerName);
            var upMethod = migrationType.GetMethod(
                "Up",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                [typeof(MigrationBuilder)],
                null) ?? throw new InvalidOperationException($"Migration '{migrationId}' does not expose Up(MigrationBuilder).");

            upMethod.Invoke(migration, [builder]);

            var sql = TranslateOperations(migrationId, builder.Operations, dialect, sqlDialect);
            results.Add(new TranslatedEfCoreMigration(migrationId, sql));
        }

        return results;
    }

    private static string TranslateOperations(
        string migrationId,
        IReadOnlyList<MigrationOperation> operations,
        ISqlDialectGenerator dialect,
        SqlDialect sqlDialect)
    {
        var wrapTx = sqlDialect == SqlDialect.PostgreSql;
        if (operations.Count == 0)
        {
            return wrapTx
                ? $"-- Translated from EF Core migration: {migrationId}{Environment.NewLine}BEGIN;{Environment.NewLine}COMMIT;{Environment.NewLine}"
                : $"-- Translated from EF Core migration: {migrationId}{Environment.NewLine}";
        }

        var body = new StringBuilder();
        var pendingSchemaOps = new List<SchemaOperation>();

        void FlushSchemaOps()
        {
            if (pendingSchemaOps.Count == 0)
            {
                return;
            }

            var generated = dialect.GenerateSql(pendingSchemaOps);
            pendingSchemaOps.Clear();
            var inner = StripTransactionWrapper(generated);
            if (!string.IsNullOrWhiteSpace(inner))
            {
                body.AppendLine(inner);
                body.AppendLine();
            }
        }

        foreach (var op in operations)
        {
            switch (op)
            {
                case EnsureSchemaOperation es:
                    pendingSchemaOps.Add(new EnsureSchemaOp(es.Name));
                    break;

                case CreateSequenceOperation cs:
                    pendingSchemaOps.Add(new CreateSequenceOp(MapSequence(sqlDialect, cs.Name, cs.Schema, cs.ClrType, cs.StartValue, cs.IncrementBy, cs.MinValue, cs.MaxValue, cs.IsCyclic)));
                    break;

                case AlterSequenceOperation alts:
                    var newSeq = MapSequence(sqlDialect, alts.Name, alts.Schema, typeof(long), 1, alts.IncrementBy, alts.MinValue, alts.MaxValue, alts.IsCyclic);
                    var oldSeq = MapSequence(sqlDialect, alts.Name, alts.Schema, typeof(long), 1, alts.OldSequence.IncrementBy, alts.OldSequence.MinValue, alts.OldSequence.MaxValue, alts.OldSequence.IsCyclic);
                    pendingSchemaOps.Add(new AlterSequenceOp(oldSeq, newSeq));
                    break;

                case DropSequenceOperation ds:
                    pendingSchemaOps.Add(new DropSequenceOp(ds.Schema, ds.Name));
                    break;

                case CreateTableOperation ct:
                    var mappedTable = MapCreateTable(ct, sqlDialect);
                    pendingSchemaOps.Add(new CreateTableOp(mappedTable));
                    if (!string.IsNullOrWhiteSpace(mappedTable.Comment))
                    {
                        pendingSchemaOps.Add(new SetTableCommentOp(mappedTable.Schema, mappedTable.TableName, mappedTable.Comment));
                    }

                    foreach (var col in mappedTable.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Comment)))
                    {
                        pendingSchemaOps.Add(new SetColumnCommentOp(mappedTable.Schema, mappedTable.TableName, col.ColumnName, col.Comment));
                    }

                    foreach (var ck in mappedTable.CheckConstraints)
                    {
                        pendingSchemaOps.Add(new AddCheckConstraintOp(mappedTable.Schema, mappedTable.TableName, ck));
                    }

                    foreach (var fk in mappedTable.ForeignKeys)
                    {
                        pendingSchemaOps.Add(new AddForeignKeyOp(mappedTable.Schema, mappedTable.TableName, fk));
                    }

                    foreach (var uc in ct.UniqueConstraints)
                    {
                        pendingSchemaOps.Add(new CreateIndexOp(ct.Schema, ct.Name, new IndexSchema
                        {
                            Name = uc.Name,
                            ColumnNames = [.. uc.Columns],
                            PropertyNames = [.. uc.Columns],
                            IsUnique = true
                        }));
                    }
                    break;

                case DropTableOperation dt:
                    pendingSchemaOps.Add(new DropTableOp(dt.Schema, dt.Name));
                    break;

                case RenameTableOperation rt:
                    pendingSchemaOps.Add(new RenameTableOp(
                        rt.Schema,
                        rt.Name,
                        rt.NewSchema ?? rt.Schema,
                        rt.NewName ?? rt.Name));
                    break;

                case AddColumnOperation ac:
                    pendingSchemaOps.Add(new AddColumnOp(ac.Schema, ac.Table, MapColumn(ac, sqlDialect)));
                    break;

                case DropColumnOperation dc:
                    pendingSchemaOps.Add(new DropColumnOp(dc.Schema, dc.Table, dc.Name));
                    break;

                case RenameColumnOperation rc:
                    pendingSchemaOps.Add(new RenameColumnOp(rc.Schema, rc.Table, rc.Name, rc.NewName));
                    break;

                case AlterColumnOperation alc:
                    var oldCol = MapAlterOldColumn(alc, sqlDialect);
                    var newCol = MapColumn(alc, sqlDialect);
                    pendingSchemaOps.Add(new AlterColumnOp(alc.Schema, alc.Table, oldCol, newCol));
                    break;

                case AddPrimaryKeyOperation apk:
                    pendingSchemaOps.Add(new AddPrimaryKeyOp(apk.Schema, apk.Table, new PrimaryKeySchema
                    {
                        Name = apk.Name,
                        ColumnNames = [.. apk.Columns],
                        PropertyNames = [.. apk.Columns]
                    }));
                    break;

                case DropPrimaryKeyOperation dpk:
                    pendingSchemaOps.Add(new DropPrimaryKeyOp(dpk.Schema, dpk.Table, dpk.Name));
                    break;

                case AddForeignKeyOperation afk:
                    pendingSchemaOps.Add(new AddForeignKeyOp(afk.Schema, afk.Table, new ForeignKeySchema
                    {
                        Name = afk.Name,
                        DependentColumns = [.. afk.Columns],
                        DependentProperties = [.. afk.Columns],
                        PrincipalSchema = afk.PrincipalSchema,
                        PrincipalTable = afk.PrincipalTable,
                        PrincipalColumns = [.. afk.PrincipalColumns ?? afk.Columns],
                        PrincipalProperties = [.. afk.PrincipalColumns ?? afk.Columns],
                        OnDelete = MapReferentialAction(afk.OnDelete)
                    }));
                    break;

                case DropForeignKeyOperation dfk:
                    pendingSchemaOps.Add(new DropForeignKeyOp(dfk.Schema, dfk.Table, dfk.Name));
                    break;

                case CreateIndexOperation ci:
                    pendingSchemaOps.Add(new CreateIndexOp(ci.Schema, ci.Table, new IndexSchema
                    {
                        Name = ci.Name,
                        ColumnNames = [.. ci.Columns],
                        PropertyNames = [.. ci.Columns],
                        IsUnique = ci.IsUnique,
                        FilterSql = ci.Filter,
                        IsDescending = ci.IsDescending?.ToList()
                    }));
                    break;

                case DropIndexOperation di:
                    pendingSchemaOps.Add(new DropIndexOp(di.Schema, di.Table ?? string.Empty, di.Name));
                    break;

                case RenameIndexOperation ri:
                    pendingSchemaOps.Add(new RenameIndexOp(ri.Schema, ri.Table ?? string.Empty, ri.Name, ri.NewName));
                    break;

                case AddCheckConstraintOperation ack:
                    pendingSchemaOps.Add(new AddCheckConstraintOp(ack.Schema, ack.Table, new CheckConstraintSchema
                    {
                        Name = ack.Name,
                        Sql = ack.Sql
                    }));
                    break;

                case DropCheckConstraintOperation dck:
                    pendingSchemaOps.Add(new DropCheckConstraintOp(dck.Schema, dck.Table, dck.Name));
                    break;

                case SqlOperation sqlOp:
                    FlushSchemaOps();
                    var rawSql = sqlOp.Sql.Trim();
                    if (!string.IsNullOrWhiteSpace(rawSql))
                    {
                        body.AppendLine(rawSql.EndsWith(';') ? rawSql : $"{rawSql};");
                        body.AppendLine();
                    }
                    break;

                case InsertDataOperation ins:
                    FlushSchemaOps();
                    AppendInsertData(body, ins);
                    body.AppendLine();
                    break;

                case UpdateDataOperation upd:
                    FlushSchemaOps();
                    AppendUpdateData(body, upd);
                    body.AppendLine();
                    break;

                case DeleteDataOperation del:
                    FlushSchemaOps();
                    AppendDeleteData(body, del);
                    body.AppendLine();
                    break;

                default:
                    FlushSchemaOps();
                    body.AppendLine($"-- Unsupported EF Core operation skipped: {op.GetType().Name}");
                    body.AppendLine();
                    break;
            }
        }

        FlushSchemaOps();

        var final = new StringBuilder();
        final.AppendLine($"-- Translated from EF Core migration: {migrationId}");
        if (wrapTx)
        {
            final.AppendLine("BEGIN;");
        }
        final.AppendLine();
        if (body.Length > 0)
        {
            final.Append(body.ToString().TrimEnd());
            final.AppendLine();
            final.AppendLine();
        }
        if (wrapTx)
        {
            final.AppendLine("COMMIT;");
        }
        return final.ToString();
    }

    private static TableSchema MapCreateTable(CreateTableOperation ct, SqlDialect sqlDialect)
    {
        var table = new TableSchema
        {
            EntityName = ct.Name,
            TableName = ct.Name,
            Schema = string.IsNullOrWhiteSpace(ct.Schema) ? null : ct.Schema,
            Comment = ct.Comment
        };

        foreach (var col in ct.Columns)
        {
            table.Columns.Add(MapColumn(col, sqlDialect));
        }

        if (ct.PrimaryKey is not null)
        {
            table.PrimaryKey = new PrimaryKeySchema
            {
                Name = ct.PrimaryKey.Name,
                ColumnNames = [.. ct.PrimaryKey.Columns],
                PropertyNames = [.. ct.PrimaryKey.Columns]
            };
        }

        foreach (var fk in ct.ForeignKeys)
        {
            table.ForeignKeys.Add(new ForeignKeySchema
            {
                Name = fk.Name,
                DependentColumns = [.. fk.Columns],
                DependentProperties = [.. fk.Columns],
                PrincipalSchema = fk.PrincipalSchema,
                PrincipalTable = fk.PrincipalTable,
                PrincipalColumns = [.. fk.PrincipalColumns ?? fk.Columns],
                PrincipalProperties = [.. fk.PrincipalColumns ?? fk.Columns],
                OnDelete = MapReferentialAction(fk.OnDelete)
            });
        }

        foreach (var ck in ct.CheckConstraints)
        {
            table.CheckConstraints.Add(new CheckConstraintSchema
            {
                Name = ck.Name,
                Sql = ck.Sql
            });
        }

        return table;
    }

    private static ColumnSchema MapColumn(ColumnOperation col, SqlDialect sqlDialect)
    {
        var strategy = col["Npgsql:ValueGenerationStrategy"]?.ToString()
                       ?? col["Oracle:Identity"]?.ToString()
                       ?? col["SqlServer:Identity"]?.ToString();
        var isIdentity = !string.IsNullOrWhiteSpace(strategy) &&
                         (strategy.Contains("Identity", StringComparison.OrdinalIgnoreCase) ||
                          strategy.Contains("Serial", StringComparison.OrdinalIgnoreCase));

        var defaultSql = !string.IsNullOrWhiteSpace(col.DefaultValueSql)
            ? col.DefaultValueSql
            : (col.DefaultValue is not null ? FormatPgLiteral(col.DefaultValue) : null);

        return new ColumnSchema
        {
            PropertyName = col.Name,
            ColumnName = col.Name,
            ClrTypeName = col.ClrType.Name,
            SqlType = SqlTypeResolver.Resolve(sqlDialect, col.ClrType, col.ColumnType, col.MaxLength, col.Precision, col.Scale, col.IsUnicode),
            IsNullable = col.IsNullable,
            MaxLength = col.MaxLength,
            Precision = col.Precision,
            Scale = col.Scale,
            IsUnicode = col.IsUnicode,
            DefaultValueSql = defaultSql,
            IsIdentity = isIdentity,
            ComputedColumnSql = col.ComputedColumnSql,
            IsComputedStored = col.IsStored ?? false,
            Comment = col.Comment,
            IsRowVersion = col.IsRowVersion
        };
    }

    private static ColumnSchema MapAlterOldColumn(AlterColumnOperation alc, SqlDialect sqlDialect)
    {
        var old = alc.OldColumn;
        var defaultSql = !string.IsNullOrWhiteSpace(old.DefaultValueSql)
            ? old.DefaultValueSql
            : (old.DefaultValue is not null ? FormatPgLiteral(old.DefaultValue) : null);

        return new ColumnSchema
        {
            PropertyName = alc.Name,
            ColumnName = alc.Name,
            ClrTypeName = old.ClrType.Name,
            SqlType = SqlTypeResolver.Resolve(sqlDialect, old.ClrType, old.ColumnType, old.MaxLength, old.Precision, old.Scale, old.IsUnicode),
            IsNullable = old.IsNullable,
            MaxLength = old.MaxLength,
            Precision = old.Precision,
            Scale = old.Scale,
            IsUnicode = old.IsUnicode,
            DefaultValueSql = defaultSql,
            ComputedColumnSql = old.ComputedColumnSql,
            IsComputedStored = old.IsStored ?? false,
            Comment = old.Comment,
            IsRowVersion = old.IsRowVersion
        };
    }

    private static SequenceSchema MapSequence(
        SqlDialect sqlDialect,
        string name,
        string? schema,
        Type clrType,
        long startValue,
        int incrementBy,
        long? minValue,
        long? maxValue,
        bool isCyclic) =>
        new()
        {
            Name = name,
            Schema = string.IsNullOrWhiteSpace(schema) ? null : schema,
            ClrTypeName = clrType.Name,
            SqlType = SqlTypeResolver.Resolve(sqlDialect, clrType, null, null, null, null, null),
            StartValue = startValue,
            IncrementBy = incrementBy,
            MinValue = minValue,
            MaxValue = maxValue,
            IsCyclic = isCyclic
        };

    private static DbReferentialAction MapReferentialAction(ReferentialAction action) =>
        action switch
        {
            ReferentialAction.Restrict => DbReferentialAction.Restrict,
            ReferentialAction.Cascade => DbReferentialAction.Cascade,
            ReferentialAction.SetNull => DbReferentialAction.SetNull,
            ReferentialAction.SetDefault => DbReferentialAction.SetDefault,
            _ => DbReferentialAction.NoAction
        };

    private static void AppendInsertData(StringBuilder sb, InsertDataOperation ins)
    {
        var rows = ins.Values.GetLength(0);
        var cols = ins.Columns.Length;
        if (rows == 0 || cols == 0)
        {
            return;
        }

        var qualified = Qualify(ins.Schema, ins.Table);
        var singleLineCols = string.Join(", ", ins.Columns.Select(Quote));

        for (var r = 0; r < rows; r++)
        {
            var vals = new string[cols];
            for (var c = 0; c < cols; c++)
            {
                vals[c] = FormatPgLiteral(ins.Values[r, c]);
            }

            var singleLineVals = string.Join(", ", vals);
            var singleLine = $"INSERT INTO {qualified} ({singleLineCols}) VALUES ({singleLineVals});";

            if (cols <= 3 && singleLine.Length <= 100)
            {
                sb.AppendLine(singleLine);
            }
            else
            {
                sb.AppendLine($"INSERT INTO {qualified} (");
                for (var c = 0; c < cols; c++)
                {
                    var comma = c < cols - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {Quote(ins.Columns[c])}{comma}");
                }
                sb.AppendLine(")");
                sb.AppendLine("VALUES (");
                for (var c = 0; c < cols; c++)
                {
                    var comma = c < cols - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {vals[c]}{comma}");
                }
                sb.AppendLine(");");
            }
        }
    }

    private static void AppendUpdateData(StringBuilder sb, UpdateDataOperation upd)
    {
        var rows = upd.Values.GetLength(0);
        if (rows == 0)
        {
            return;
        }

        var qualified = Qualify(upd.Schema, upd.Table);
        for (var r = 0; r < rows; r++)
        {
            var sets = new string[upd.Columns.Length];
            for (var c = 0; c < upd.Columns.Length; c++)
            {
                sets[c] = $"{Quote(upd.Columns[c])} = {FormatPgLiteral(upd.Values[r, c])}";
            }

            var wheres = new string[upd.KeyColumns.Length];
            for (var k = 0; k < upd.KeyColumns.Length; k++)
            {
                var kv = upd.KeyValues[r, k];
                wheres[k] = kv is null
                    ? $"{Quote(upd.KeyColumns[k])} IS NULL"
                    : $"{Quote(upd.KeyColumns[k])} = {FormatPgLiteral(kv)}";
            }

            var singleLine = $"UPDATE {qualified} SET {string.Join(", ", sets)} WHERE {string.Join(" AND ", wheres)};";
            if (sets.Length <= 2 && wheres.Length == 1 && singleLine.Length <= 100)
            {
                sb.AppendLine(singleLine);
            }
            else
            {
                sb.AppendLine($"UPDATE {qualified}");
                sb.AppendLine("SET");
                for (var c = 0; c < sets.Length; c++)
                {
                    var comma = c < sets.Length - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {sets[c]}{comma}");
                }
                sb.AppendLine("WHERE");
                for (var k = 0; k < wheres.Length; k++)
                {
                    var suffix = k < wheres.Length - 1 ? " AND" : ";";
                    sb.AppendLine($"    {wheres[k]}{suffix}");
                }
            }
        }
    }

    private static void AppendDeleteData(StringBuilder sb, DeleteDataOperation del)
    {
        var rows = del.KeyValues.GetLength(0);
        if (rows == 0)
        {
            return;
        }

        var qualified = Qualify(del.Schema, del.Table);
        for (var r = 0; r < rows; r++)
        {
            var wheres = new string[del.KeyColumns.Length];
            for (var k = 0; k < del.KeyColumns.Length; k++)
            {
                var kv = del.KeyValues[r, k];
                wheres[k] = kv is null
                    ? $"{Quote(del.KeyColumns[k])} IS NULL"
                    : $"{Quote(del.KeyColumns[k])} = {FormatPgLiteral(kv)}";
            }

            var singleLine = $"DELETE FROM {qualified} WHERE {string.Join(" AND ", wheres)};";
            if (wheres.Length == 1 && singleLine.Length <= 100)
            {
                sb.AppendLine(singleLine);
            }
            else
            {
                sb.AppendLine($"DELETE FROM {qualified}");
                sb.AppendLine("WHERE");
                for (var k = 0; k < wheres.Length; k++)
                {
                    var suffix = k < wheres.Length - 1 ? " AND" : ";";
                    sb.AppendLine($"    {wheres[k]}{suffix}");
                }
            }
        }
    }

    private static string FormatPgLiteral(object? value)
    {
        if (value is null || value is DBNull) return "NULL";
        return value switch
        {
            bool b => b ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
            Guid g => $"'{g:D}'",
            DateTimeOffset dto => $"'{dto.ToUniversalTime():yyyy-MM-dd HH:mm:ss.ffffff+00:00}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.ffffff}'",
            DateOnly d => $"'{d:yyyy-MM-dd}'",
            TimeOnly t => $"'{t:HH:mm:ss.ffffff}'",
            byte[] bytes => $"'\\x{Convert.ToHexStringLower(bytes)}'",
            Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => $"'{value.ToString()!.Replace("'", "''")}'"
        };
    }

    private static string StripTransactionWrapper(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[0].Trim().Equals("BEGIN;", StringComparison.OrdinalIgnoreCase))
        {
            lines.RemoveAt(0);
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (trimmed.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
            }
            break;
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }
}
