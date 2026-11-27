using System.Text.RegularExpressions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static partial class SqlPermissionRequirementExtractor
{
    private const string Ident = @"(?:""([^""]+)""|([A-Za-z_][A-Za-z0-9_$]*))";
    private const string Qualified = $@"(?:{Ident}\s*\.\s*)?{Ident}";

    [GeneratedRegex(@"--[^\r\n]*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentsRegex();

    [GeneratedRegex(@"EXECUTE\s+IMMEDIATE\s+'((?:''|[^'])*)'", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ExecuteImmediateRegex();

    [GeneratedRegex($@"\bCREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?{Ident}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CreateSchemaRegex();

    [GeneratedRegex($@"\bCREATE\s+(?:UNLOGGED\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CreateTableRegex();

    [GeneratedRegex($@"\bALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AlterTableRegex();

    [GeneratedRegex($@"\bDROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DropTableRegex();

    [GeneratedRegex($@"\bCREATE\s+SEQUENCE\s+(?:IF\s+NOT\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CreateSequenceRegex();

    [GeneratedRegex($@"\bALTER\s+SEQUENCE\s+(?:IF\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AlterSequenceRegex();

    [GeneratedRegex($@"\bDROP\s+SEQUENCE\s+(?:IF\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DropSequenceRegex();

    [GeneratedRegex($@"\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?:{Qualified}\s+)?ON\s+{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CreateIndexRegex();

    [GeneratedRegex($@"\bDROP\s+INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DropIndexRegex();

    [GeneratedRegex($@"\b(?:CREATE|ALTER|DROP)\s+TYPE\s+(?:IF\s+EXISTS\s+)?{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TypeDdlRegex();

    [GeneratedRegex($@"\bCREATE\s+EXTENSION\s+(?:IF\s+NOT\s+EXISTS\s+)?{Ident}(?:\s+WITH\s+SCHEMA\s+{Ident})?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CreateExtensionRegex();

    [GeneratedRegex($@"(?:\bCONSTRAINT\s+{Ident}\s+)?FOREIGN\s+KEY\s*\([^)]+\)\s*REFERENCES\s+{Qualified}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ForeignKeyRefRegex();

    [GeneratedRegex($@"\bTABLESPACE\s+{Ident}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TablespaceRegex();

    public static DatabasePermissionRequirements Extract(
        IEnumerable<string> scriptBodies,
        DbMigratorOptions options,
        IMigrationDialect dialect,
        bool historyTableExists)
    {
        var defaultSchema = dialect.NormalizeSchemaName(null);
        var historySchema = string.IsNullOrWhiteSpace(options.HistorySchema)
            ? defaultSchema
            : dialect.NormalizeSchemaName(options.HistorySchema);

        var req = new DatabasePermissionRequirements
        {
            HistorySchema = string.IsNullOrWhiteSpace(historySchema) ? null : historySchema,
            HistoryTableName = options.HistoryTableName,
            RequiresHistoryTableCreation = !historyTableExists
        };

        if (!historyTableExists)
        {
            AddSchema(req.TableCreateSchemas, historySchema);
            AddSchema(req.SchemasTouched, historySchema);
            if (!string.IsNullOrWhiteSpace(options.HistorySchema))
            {
                req.SchemasToCreate.Add(historySchema);
            }
        }

        foreach (var rawSql in scriptBodies)
        {
            if (string.IsNullOrWhiteSpace(rawSql))
            {
                continue;
            }

            var cleaned = CommentsRegex().Replace(rawSql, " ");
            AnalyzeSqlBlock(cleaned, defaultSchema, req);

            foreach (Match m in ExecuteImmediateRegex().Matches(cleaned))
            {
                var innerSql = m.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
                AnalyzeSqlBlock(innerSql, defaultSchema, req);
            }
        }

        return req;
    }

    private static void AnalyzeSqlBlock(string sql, string defaultSchema, DatabasePermissionRequirements req)
    {
        foreach (Match m in CreateSchemaRegex().Matches(sql))
        {
            var schema = FirstNonEmpty(m, 1, 2);
            if (!string.IsNullOrWhiteSpace(schema))
            {
                req.SchemasToCreate.Add(schema);
                req.SchemasTouched.Add(schema);
            }
        }

        string? lastDependentSchema = null;
        foreach (Match m in CreateTableRegex().Matches(sql))
        {
            var (schema, _) = ParseQualified(m, 1, defaultSchema);
            lastDependentSchema = schema;
            AddSchema(req.TableCreateSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in AlterTableRegex().Matches(sql))
        {
            var (schema, name) = ParseQualified(m, 1, defaultSchema);
            lastDependentSchema = schema;
            req.TableAlters.Add(new QualifiedObjectName(schema, name));
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in DropTableRegex().Matches(sql))
        {
            var (schema, name) = ParseQualified(m, 1, defaultSchema);
            req.TableDrops.Add(new QualifiedObjectName(schema, name));
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in CreateSequenceRegex().Matches(sql))
        {
            var (schema, _) = ParseQualified(m, 1, defaultSchema);
            AddSchema(req.SequenceCreateSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in AlterSequenceRegex().Matches(sql))
        {
            var (schema, _) = ParseQualified(m, 1, defaultSchema);
            AddSchema(req.SequenceAlterSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in DropSequenceRegex().Matches(sql))
        {
            var (schema, _) = ParseQualified(m, 1, defaultSchema);
            AddSchema(req.SequenceDropSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in CreateIndexRegex().Matches(sql))
        {
            var (schema, table) = ParseQualified(m, 5, defaultSchema);
            req.IndexCreateTables.Add(new QualifiedObjectName(schema, table));
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in DropIndexRegex().Matches(sql))
        {
            var (schema, _) = ParseQualified(m, 1, defaultSchema);
            AddSchema(req.IndexDropSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in TypeDdlRegex().Matches(sql))
        {
            var (schema, _) = ParseQualified(m, 1, defaultSchema);
            AddSchema(req.TypeSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in CreateExtensionRegex().Matches(sql))
        {
            var extName = FirstNonEmpty(m, 1, 2);
            if (!string.IsNullOrWhiteSpace(extName))
            {
                req.Extensions.Add(extName);
            }

            var extSchema = FirstNonEmpty(m, 3, 4);
            if (!string.IsNullOrWhiteSpace(extSchema))
            {
                AddSchema(req.SchemasTouched, extSchema);
            }
        }

        foreach (Match m in ForeignKeyRefRegex().Matches(sql))
        {
            var constraintName = FirstNonEmpty(m, 1, 2);
            var (prinSchema, prinTable) = ParseQualified(m, 3, defaultSchema);
            if (!string.IsNullOrWhiteSpace(prinTable))
            {
                req.ForeignKeyReferences.Add(new ForeignKeyReferenceRequirement(
                    lastDependentSchema,
                    prinSchema,
                    prinTable,
                    string.IsNullOrWhiteSpace(constraintName) ? null : constraintName));
                AddSchema(req.SchemasTouched, prinSchema);
            }
        }

        foreach (Match m in TablespaceRegex().Matches(sql))
        {
            var ts = FirstNonEmpty(m, 1, 2);
            if (!string.IsNullOrWhiteSpace(ts))
            {
                req.Tablespaces.Add(ts);
            }
        }
    }

    private static (string? Schema, string Name) ParseQualified(Match m, int startGroup, string defaultSchema)
    {
        var part1 = FirstNonEmpty(m, startGroup, startGroup + 1);
        var part2 = FirstNonEmpty(m, startGroup + 2, startGroup + 3);
        if (!string.IsNullOrWhiteSpace(part1))
        {
            return (part1, part2);
        }

        return (string.IsNullOrWhiteSpace(defaultSchema) ? null : defaultSchema, part2);
    }

    private static string FirstNonEmpty(Match m, int g1, int g2) =>
        m.Groups[g1].Success ? m.Groups[g1].Value : (m.Groups[g2].Success ? m.Groups[g2].Value : string.Empty);

    private static void AddSchema(HashSet<string> set, string? schema) =>
        set.Add(schema ?? string.Empty);
}
