using System.Text.RegularExpressions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal static class SqlPermissionRequirementExtractor
{
    public static DatabasePermissionRequirements Create(
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
            HistoryTableName = DbMigratorConsts.HistoryTableName,
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

        return req;
    }

    public static DatabasePermissionRequirements Extract(
        IEnumerable<string> scriptBodies,
        DbMigratorOptions options,
        IMigrationDialect dialect,
        bool historyTableExists)
    {
        var req = Create(options, dialect, historyTableExists);
        foreach (var rawSql in scriptBodies)
        {
            AddScript(req, rawSql, dialect);
        }

        return req;
    }

    public static void AddScript(DatabasePermissionRequirements req, string rawSql, IMigrationDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
        {
            return;
        }

        var defaultSchema = dialect.NormalizeSchemaName(null);
        var split = dialect.TryStripOuterTransaction(rawSql, out var innerSql)
            ? dialect.SplitScriptStatements(innerSql)
            : dialect.SplitScriptStatements(rawSql);

        foreach (var stmt in SqlDdlPatterns.EnumerateSanitizedStatements(split))
        {
            AnalyzeStatement(stmt, defaultSchema, req);
        }
    }

    private static void AnalyzeStatement(string stmt, string defaultSchema, DatabasePermissionRequirements req)
    {
        foreach (Match m in SqlDdlPatterns.CreateSchema().Matches(stmt))
        {
            var schema = SqlDdlPatterns.FirstNonEmpty(m, 1, 2);
            if (!string.IsNullOrWhiteSpace(schema))
            {
                req.SchemasToCreate.Add(schema);
                req.SchemasTouched.Add(schema);
            }
        }

        string? dependentSchema = null;
        foreach (Match m in SqlDdlPatterns.CreateTable().Matches(stmt))
        {
            var (schema, _) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            dependentSchema = schema;
            AddSchema(req.TableCreateSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.AlterTable().Matches(stmt))
        {
            var (schema, name) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            dependentSchema = schema;
            req.TableAlters.Add(new QualifiedObjectName(schema, name));
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.DropTable().Matches(stmt))
        {
            var (schema, name) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            req.TableDrops.Add(new QualifiedObjectName(schema, name));
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.CreateSequence().Matches(stmt))
        {
            var (schema, _) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            AddSchema(req.SequenceCreateSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.AlterSequence().Matches(stmt))
        {
            var (schema, _) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            AddSchema(req.SequenceAlterSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.DropSequence().Matches(stmt))
        {
            var (schema, _) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            AddSchema(req.SequenceDropSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.CreateIndex().Matches(stmt))
        {
            var (schema, table) = SqlDdlPatterns.ParseQualified(m, 5, defaultSchema);
            req.IndexCreateTables.Add(new QualifiedObjectName(schema, table));
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.DropIndex().Matches(stmt))
        {
            var (schema, _) = SqlDdlPatterns.ParseQualified(m, 1, defaultSchema);
            AddSchema(req.IndexDropSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.TypeDdl().Matches(stmt))
        {
            var (schema, _) = SqlDdlPatterns.ParseQualified(m, 2, defaultSchema);
            AddSchema(req.TypeSchemas, schema);
            AddSchema(req.SchemasTouched, schema);
        }

        foreach (Match m in SqlDdlPatterns.CreateExtension().Matches(stmt))
        {
            var extName = SqlDdlPatterns.FirstNonEmpty(m, 1, 2);
            if (!string.IsNullOrWhiteSpace(extName))
            {
                req.Extensions.Add(extName);
            }

            var extSchema = SqlDdlPatterns.FirstNonEmpty(m, 3, 4);
            if (!string.IsNullOrWhiteSpace(extSchema))
            {
                AddSchema(req.SchemasTouched, extSchema);
            }
        }

        foreach (Match m in SqlDdlPatterns.ForeignKeyReference().Matches(stmt))
        {
            var constraintName = SqlDdlPatterns.FirstNonEmpty(m, 1, 2);
            var (prinSchema, prinTable) = SqlDdlPatterns.ParseQualified(m, 3, defaultSchema);
            if (!string.IsNullOrWhiteSpace(prinTable))
            {
                req.ForeignKeyReferences.Add(new ForeignKeyReferenceRequirement(
                    dependentSchema,
                    prinSchema,
                    prinTable,
                    string.IsNullOrWhiteSpace(constraintName) ? null : constraintName));
                AddSchema(req.SchemasTouched, prinSchema);
            }
        }

        foreach (Match m in SqlDdlPatterns.Tablespace().Matches(stmt))
        {
            var ts = SqlDdlPatterns.FirstNonEmpty(m, 1, 2);
            if (!string.IsNullOrWhiteSpace(ts))
            {
                req.Tablespaces.Add(ts);
            }
        }
    }

    private static void AddSchema(HashSet<string> set, string? schema) =>
        set.Add(schema ?? string.Empty);
}
