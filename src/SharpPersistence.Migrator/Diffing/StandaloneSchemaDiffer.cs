using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Diffing;

internal static class StandaloneSchemaDiffer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static SchemaDiffResult GenerateDiff(
        DatabaseModel currentSchema,
        string? existingSnapshotJson,
        SqlDialect sqlDialect = SqlDialect.PostgreSql,
        bool interactiveRenamePrompt = true,
        OracleDbCompatibility oracleCompatibility = OracleDbCompatibility.Oracle19,
        Func<string, bool>? confirmRename = null)
    {
        var migratableCurrent = FilterMigratableSchema(currentSchema);
        DatabaseModel? previousSchema = null;
        if (!string.IsNullOrWhiteSpace(existingSnapshotJson))
        {
            previousSchema = JsonSerializer.Deserialize<DatabaseModel>(existingSnapshotJson, JsonOptions);
        }

        var prompter = interactiveRenamePrompt ? confirmRename : null;
        var operations = ComputeOperations(previousSchema, migratableCurrent, prompter, sqlDialect);
        if (operations.Count == 0)
        {
            return new SchemaDiffResult(false, string.Empty, string.Empty, existingSnapshotJson ?? string.Empty);
        }

        var mainOps = new List<SchemaOperation>(operations.Count);
        var deferredOps = new List<SchemaOperation>();

        foreach (var op in operations)
        {
            if (op is AddForeignKeyOp { ForeignKey.IsCircular: true })
            {
                deferredOps.Add(op);
            }
            else
            {
                mainOps.Add(op);
            }
        }

        var generator = MigrationDialectRegistry.Get(sqlDialect, oracleCompatibility).CreateDdlGenerator();

        var sql = mainOps.Count > 0 ? generator.GenerateSql(mainOps) : string.Empty;
        var deferredSql = deferredOps.Count > 0 ? generator.GenerateSql(deferredOps) : string.Empty;
        var updatedJson = JsonSerializer.Serialize(migratableCurrent, JsonOptions);

        return new SchemaDiffResult(true, sql, deferredSql, updatedJson);
    }

    internal static int CountPendingOperations(
        DatabaseModel currentSchema,
        string? existingSnapshotJson,
        SqlDialect sqlDialect = SqlDialect.PostgreSql)
    {
        var migratableCurrent = FilterMigratableSchema(currentSchema);
        DatabaseModel? previousSchema = null;
        if (!string.IsNullOrWhiteSpace(existingSnapshotJson))
        {
            previousSchema = JsonSerializer.Deserialize<DatabaseModel>(existingSnapshotJson, JsonOptions);
        }

        return ComputeOperations(previousSchema, migratableCurrent, confirmRename: null, sqlDialect).Count;
    }

    private static DatabaseModel FilterMigratableSchema(DatabaseModel schema)
    {
        if (schema.Tables.All(t => !t.ExcludeFromMigrations))
        {
            return schema;
        }

        return new DatabaseModel
        {
            Extensions = schema.Extensions,
            Enums = schema.Enums,
            Sequences = schema.Sequences,
            Tables = schema.Tables.Where(t => !t.ExcludeFromMigrations).ToList()
        };
    }

    private static List<SchemaOperation> ComputeOperations(
        DatabaseModel? oldSchema,
        DatabaseModel newSchema,
        Func<string, bool>? confirmRename,
        SqlDialect sqlDialect)
    {
        var ops = new List<SchemaOperation>();

        if (oldSchema is null)
        {
            if (newSchema.Extensions is { Count: > 0 })
            {
                foreach (var ext in newSchema.Extensions)
                {
                    if (!string.IsNullOrWhiteSpace(ext.Schema))
                    {
                        ops.Add(new EnsureSchemaOp(ext.Schema));
                    }

                    ops.Add(new EnsureExtensionOp(ext.Name, ext.Schema));
                }
            }

            if (newSchema.Enums is { Count: > 0 })
            {
                foreach (var pgEnum in newSchema.Enums)
                {
                    if (!string.IsNullOrWhiteSpace(pgEnum.Schema))
                    {
                        ops.Add(new EnsureSchemaOp(pgEnum.Schema));
                    }

                    ops.Add(new CreateEnumOp(pgEnum));
                }
            }

            var createdSchemas = new HashSet<string>(StringComparer.Ordinal);
            if (newSchema.Sequences is { Count: > 0 })
            {
                foreach (var seq in newSchema.Sequences)
                {
                    if (!string.IsNullOrWhiteSpace(seq.Schema) && createdSchemas.Add(seq.Schema))
                    {
                        ops.Add(new EnsureSchemaOp(seq.Schema));
                    }

                    ops.Add(new CreateSequenceOp(seq));
                }
            }

            foreach (var table in newSchema.Tables)
            {
                if (!string.IsNullOrWhiteSpace(table.Schema) && createdSchemas.Add(table.Schema))
                {
                    ops.Add(new EnsureSchemaOp(table.Schema));
                }

                ops.Add(new CreateTableOp(table));
                if (!string.IsNullOrWhiteSpace(table.Comment))
                {
                    ops.Add(new SetTableCommentOp(table.Schema, table.TableName, table.Comment));
                }

                foreach (var col in table.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(col.Comment))
                    {
                        ops.Add(new SetColumnCommentOp(table.Schema, table.TableName, col.ColumnName, col.Comment));
                    }
                }

                foreach (var ix in table.Indexes)
                {
                    ops.Add(new CreateIndexOp(table.Schema, table.TableName, ix));
                }
            }

            foreach (var table in newSchema.Tables)
            {
                foreach (var fk in table.ForeignKeys)
                {
                    ops.Add(new AddForeignKeyOp(table.Schema, table.TableName, fk));
                }

                foreach (var ck in table.CheckConstraints)
                {
                    ops.Add(new AddCheckConstraintOp(table.Schema, table.TableName, ck));
                }

                if (table.ExclusionConstraints is { Count: > 0 })
                {
                    foreach (var ex in table.ExclusionConstraints)
                    {
                        ops.Add(new AddExclusionConstraintOp(table.Schema, table.TableName, ex));
                    }
                }
            }

            return ops;
        }

        var oldExts = (oldSchema.Extensions ?? []).ToDictionary(e => e.Name, StringComparer.Ordinal);
        var newExts = (newSchema.Extensions ?? []).ToDictionary(e => e.Name, StringComparer.Ordinal);

        foreach (var (name, _) in oldExts)
        {
            if (!newExts.ContainsKey(name))
            {
                ops.Add(new DropExtensionOp(name));
            }
        }

        foreach (var (name, newExt) in newExts)
        {
            if (!oldExts.TryGetValue(name, out var oldExt) ||
                !string.Equals(oldExt.Schema, newExt.Schema, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(newExt.Schema))
                {
                    ops.Add(new EnsureSchemaOp(newExt.Schema));
                }

                ops.Add(new EnsureExtensionOp(newExt.Name, newExt.Schema));
            }
        }

        var oldEnums = (oldSchema.Enums ?? []).ToDictionary(e => e.QualifiedName, StringComparer.Ordinal);
        var newEnums = (newSchema.Enums ?? []).ToDictionary(e => e.QualifiedName, StringComparer.Ordinal);
        var droppedEnums = new List<DropEnumOp>();

        foreach (var (key, oldEnum) in oldEnums)
        {
            if (!newEnums.ContainsKey(key))
            {
                droppedEnums.Add(new DropEnumOp(oldEnum.Schema, oldEnum.Name));
            }
        }

        foreach (var (key, newEnum) in newEnums)
        {
            if (!oldEnums.TryGetValue(key, out var oldEnum))
            {
                if (!string.IsNullOrWhiteSpace(newEnum.Schema))
                {
                    ops.Add(new EnsureSchemaOp(newEnum.Schema));
                }

                ops.Add(new CreateEnumOp(newEnum));
            }
            else
            {
                var existingLabels = oldEnum.Labels.ToHashSet(StringComparer.Ordinal);
                foreach (var label in newEnum.Labels)
                {
                    if (!existingLabels.Contains(label))
                    {
                        ops.Add(new AddEnumValueOp(newEnum.Schema, newEnum.Name, label));
                    }
                }
            }
        }

        var oldSeqs = (oldSchema.Sequences ?? []).ToDictionary(s => s.QualifiedName, StringComparer.Ordinal);
        var newSeqs = (newSchema.Sequences ?? []).ToDictionary(s => s.QualifiedName, StringComparer.Ordinal);
        var seqSchemas = new HashSet<string>(StringComparer.Ordinal);
        var droppedSeqs = new List<DropSequenceOp>();

        foreach (var (key, oldSeq) in oldSeqs)
        {
            if (!newSeqs.ContainsKey(key))
            {
                droppedSeqs.Add(new DropSequenceOp(oldSeq.Schema, oldSeq.Name));
            }
        }

        foreach (var (key, newSeq) in newSeqs)
        {
            if (!oldSeqs.TryGetValue(key, out var oldSeq))
            {
                if (!string.IsNullOrWhiteSpace(newSeq.Schema) && seqSchemas.Add(newSeq.Schema))
                {
                    ops.Add(new EnsureSchemaOp(newSeq.Schema));
                }

                ops.Add(new CreateSequenceOp(newSeq));
            }
            else if (oldSeq.IncrementBy != newSeq.IncrementBy ||
                     oldSeq.MinValue != newSeq.MinValue ||
                     oldSeq.MaxValue != newSeq.MaxValue ||
                     oldSeq.IsCyclic != newSeq.IsCyclic)
            {
                ops.Add(new AlterSequenceOp(oldSeq, newSeq));
            }
        }

        var unmatchedOld = new List<TableSchema>(oldSchema.Tables);
        var unmatchedNew = new List<TableSchema>(newSchema.Tables);
        var matchedPairs = new List<(TableSchema Old, TableSchema New)>();
        var renamedTableMap = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = unmatchedOld.Count - 1; i >= 0; i--)
        {
            var o = unmatchedOld[i];
            var n = unmatchedNew.FirstOrDefault(x =>
                string.Equals(x.QualifiedName, o.QualifiedName, StringComparison.Ordinal));
            if (n is not null)
            {
                matchedPairs.Add((o, n));
                unmatchedOld.RemoveAt(i);
                unmatchedNew.Remove(n);
            }
        }

        for (var i = unmatchedOld.Count - 1; i >= 0; i--)
        {
            var o = unmatchedOld[i];
            var n = unmatchedNew.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(x.PreviousTableName) &&
                 string.Equals(x.PreviousTableName, o.TableName, StringComparison.Ordinal) &&
                 (x.PreviousSchema is null || string.Equals(x.PreviousSchema, o.Schema, StringComparison.Ordinal))) ||
                string.Equals(x.EntityName, o.EntityName, StringComparison.Ordinal));

            if (n is not null)
            {
                ops.Add(new RenameTableOp(o.Schema, o.TableName, n.Schema, n.TableName));
                renamedTableMap[o.QualifiedName] = n.QualifiedName;
                matchedPairs.Add((o, n));
                unmatchedOld.RemoveAt(i);
                unmatchedNew.Remove(n);
            }
        }

        if (confirmRename is not null && unmatchedOld.Count > 0 && unmatchedNew.Count > 0)
        {
            for (var i = unmatchedOld.Count - 1; i >= 0; i--)
            {
                var o = unmatchedOld[i];
                for (var j = 0; j < unmatchedNew.Count; j++)
                {
                    var n = unmatchedNew[j];
                    if (confirmRename($"Was table '{o.QualifiedName}' renamed to '{n.QualifiedName}'?"))
                    {
                        ops.Add(new RenameTableOp(o.Schema, o.TableName, n.Schema, n.TableName));
                        renamedTableMap[o.QualifiedName] = n.QualifiedName;
                        matchedPairs.Add((o, n));
                        unmatchedOld.RemoveAt(i);
                        unmatchedNew.RemoveAt(j);
                        break;
                    }
                }
            }
        }

        foreach (var dropped in unmatchedOld)
        {
            ops.Add(new DropTableOp(dropped.Schema, dropped.TableName));
        }

        var ensuredSchemas = new HashSet<string>(StringComparer.Ordinal);
        var pendingForeignKeys = new List<AddForeignKeyOp>();
        foreach (var newTable in unmatchedNew)
        {
            if (!string.IsNullOrWhiteSpace(newTable.Schema) && ensuredSchemas.Add(newTable.Schema))
            {
                ops.Add(new EnsureSchemaOp(newTable.Schema));
            }

            ops.Add(new CreateTableOp(newTable));
            if (!string.IsNullOrWhiteSpace(newTable.Comment))
            {
                ops.Add(new SetTableCommentOp(newTable.Schema, newTable.TableName, newTable.Comment));
            }

            foreach (var col in newTable.Columns)
            {
                if (!string.IsNullOrWhiteSpace(col.Comment))
                {
                    ops.Add(new SetColumnCommentOp(newTable.Schema, newTable.TableName, col.ColumnName, col.Comment));
                }
            }

            foreach (var ix in newTable.Indexes)
            {
                ops.Add(new CreateIndexOp(newTable.Schema, newTable.TableName, ix));
            }

            foreach (var fk in newTable.ForeignKeys)
            {
                pendingForeignKeys.Add(new AddForeignKeyOp(newTable.Schema, newTable.TableName, fk));
            }

            foreach (var ck in newTable.CheckConstraints)
            {
                ops.Add(new AddCheckConstraintOp(newTable.Schema, newTable.TableName, ck));
            }

            if (newTable.ExclusionConstraints is { Count: > 0 })
            {
                foreach (var ex in newTable.ExclusionConstraints)
                {
                    ops.Add(new AddExclusionConstraintOp(newTable.Schema, newTable.TableName, ex));
                }
            }
        }

        foreach (var (oldTable, newTable) in matchedPairs)
        {
            DiffTable(oldTable, newTable, ops, pendingForeignKeys, confirmRename, renamedTableMap, sqlDialect);
        }

        ops.AddRange(pendingForeignKeys);
        ops.AddRange(droppedSeqs);
        ops.AddRange(droppedEnums);

        return ops;
    }

    private static void DiffTable(
        TableSchema oldTable,
        TableSchema newTable,
        List<SchemaOperation> ops,
        List<AddForeignKeyOp> pendingForeignKeys,
        Func<string, bool>? confirmRename,
        IReadOnlyDictionary<string, string> renamedTableMap,
        SqlDialect sqlDialect)
    {
        var dropPk = false;
        var addPk = false;
        if (oldTable.PrimaryKey is not null && newTable.PrimaryKey is not null)
        {
            if (oldTable.PrimaryKey.PropertyNames.SequenceEqual(newTable.PrimaryKey.PropertyNames, StringComparer.Ordinal))
            {
                if (!string.Equals(oldTable.PrimaryKey.Name, newTable.PrimaryKey.Name, StringComparison.Ordinal))
                {
                    ops.Add(new RenameConstraintOp(
                        newTable.Schema,
                        newTable.TableName,
                        oldTable.PrimaryKey.Name,
                        newTable.PrimaryKey.Name));
                }
            }
            else
            {
                dropPk = true;
                addPk = true;
            }
        }
        else if (oldTable.PrimaryKey is not null)
        {
            dropPk = true;
        }
        else if (newTable.PrimaryKey is not null)
        {
            addPk = true;
        }

        if (dropPk && oldTable.PrimaryKey is not null)
        {
            ops.Add(new DropPrimaryKeyOp(newTable.Schema, newTable.TableName, oldTable.PrimaryKey.Name));
        }

        var oldCks = new List<CheckConstraintSchema>(oldTable.CheckConstraints);
        var newCks = new List<CheckConstraintSchema>(newTable.CheckConstraints);

        for (var i = oldCks.Count - 1; i >= 0; i--)
        {
            var o = oldCks[i];
            var exact = newCks.FirstOrDefault(n =>
                string.Equals(o.Name, n.Name, StringComparison.Ordinal) &&
                string.Equals(o.Sql, n.Sql, StringComparison.Ordinal));
            if (exact is not null)
            {
                oldCks.RemoveAt(i);
                newCks.Remove(exact);
            }
        }

        for (var i = oldCks.Count - 1; i >= 0; i--)
        {
            var o = oldCks[i];
            var renamed = newCks.FirstOrDefault(n => string.Equals(o.Sql, n.Sql, StringComparison.Ordinal));
            if (renamed is not null)
            {
                ops.Add(new RenameConstraintOp(newTable.Schema, newTable.TableName, o.Name, renamed.Name));
                oldCks.RemoveAt(i);
                newCks.Remove(renamed);
            }
        }

        foreach (var droppedCk in oldCks)
        {
            ops.Add(new DropCheckConstraintOp(newTable.Schema, newTable.TableName, droppedCk.Name));
        }

        var oldExs = new List<ExclusionConstraintSchema>(oldTable.ExclusionConstraints ?? []);
        var newExs = new List<ExclusionConstraintSchema>(newTable.ExclusionConstraints ?? []);

        for (var i = oldExs.Count - 1; i >= 0; i--)
        {
            var o = oldExs[i];
            var exact = newExs.FirstOrDefault(n =>
                string.Equals(o.Name, n.Name, StringComparison.Ordinal) &&
                ExclusionConstraintEquals(o, n));
            if (exact is not null)
            {
                oldExs.RemoveAt(i);
                newExs.Remove(exact);
            }
        }

        for (var i = oldExs.Count - 1; i >= 0; i--)
        {
            var o = oldExs[i];
            var renamed = newExs.FirstOrDefault(n => ExclusionConstraintEquals(o, n));
            if (renamed is not null)
            {
                ops.Add(new RenameConstraintOp(newTable.Schema, newTable.TableName, o.Name, renamed.Name));
                oldExs.RemoveAt(i);
                newExs.Remove(renamed);
            }
        }

        foreach (var droppedEx in oldExs)
        {
            ops.Add(new DropExclusionConstraintOp(newTable.Schema, newTable.TableName, droppedEx.Name));
        }

        var oldFks = new List<ForeignKeySchema>(oldTable.ForeignKeys);
        var newFks = new List<ForeignKeySchema>(newTable.ForeignKeys);

        for (var i = oldFks.Count - 1; i >= 0; i--)
        {
            var o = oldFks[i];
            var exact = newFks.FirstOrDefault(n =>
                string.Equals(o.Name, n.Name, StringComparison.Ordinal) &&
                ForeignKeyEquals(o, n, renamedTableMap));
            if (exact is not null)
            {
                oldFks.RemoveAt(i);
                newFks.Remove(exact);
            }
        }

        for (var i = oldFks.Count - 1; i >= 0; i--)
        {
            var o = oldFks[i];
            var renamed = newFks.FirstOrDefault(n => ForeignKeyEquals(o, n, renamedTableMap));
            if (renamed is not null)
            {
                ops.Add(new RenameConstraintOp(newTable.Schema, newTable.TableName, o.Name, renamed.Name));
                oldFks.RemoveAt(i);
                newFks.Remove(renamed);
            }
        }

        foreach (var droppedFk in oldFks)
        {
            ops.Add(new DropForeignKeyOp(newTable.Schema, newTable.TableName, droppedFk.Name));
        }

        var addedIndexes = DiffIndexesDropAndRename(oldTable, newTable, ops);
        DiffColumns(oldTable, newTable, ops, confirmRename, sqlDialect);

        if (addPk && newTable.PrimaryKey is not null)
        {
            ops.Add(new AddPrimaryKeyOp(newTable.Schema, newTable.TableName, newTable.PrimaryKey));
        }

        foreach (var addedIx in addedIndexes)
        {
            ops.Add(new CreateIndexOp(newTable.Schema, newTable.TableName, addedIx));
        }

        foreach (var addedFk in newFks)
        {
            pendingForeignKeys.Add(new AddForeignKeyOp(newTable.Schema, newTable.TableName, addedFk));
        }

        foreach (var addedCk in newCks)
        {
            ops.Add(new AddCheckConstraintOp(newTable.Schema, newTable.TableName, addedCk));
        }

        foreach (var addedEx in newExs)
        {
            ops.Add(new AddExclusionConstraintOp(newTable.Schema, newTable.TableName, addedEx));
        }

        if (!string.Equals(oldTable.Comment, newTable.Comment, StringComparison.Ordinal))
        {
            ops.Add(new SetTableCommentOp(newTable.Schema, newTable.TableName, newTable.Comment));
        }

        if (oldTable.IsUnlogged != newTable.IsUnlogged)
        {
            ops.Add(new SetTableLoggedOp(newTable.Schema, newTable.TableName, newTable.IsUnlogged));
        }

        if (!string.Equals(oldTable.Tablespace, newTable.Tablespace, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(newTable.Tablespace))
        {
            ops.Add(new SetTableTablespaceOp(newTable.Schema, newTable.TableName, newTable.Tablespace));
        }

        DiffStorageParameters(oldTable, newTable, ops);
    }

    private static void DiffStorageParameters(TableSchema oldTable, TableSchema newTable, List<SchemaOperation> ops)
    {
        var oldParams = oldTable.StorageParameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newParams = newTable.StorageParameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var toSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, val) in newParams.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!oldParams.TryGetValue(key, out var oldVal) || !string.Equals(oldVal, val, StringComparison.Ordinal))
            {
                toSet[key] = val;
            }
        }

        var toReset = oldParams.Keys
            .Where(k => !newParams.ContainsKey(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (toSet.Count > 0 || toReset.Count > 0)
        {
            ops.Add(new AlterTableStorageParametersOp(newTable.Schema, newTable.TableName, toSet, toReset));
        }
    }

    private static void DiffColumns(
        TableSchema oldTable,
        TableSchema newTable,
        List<SchemaOperation> ops,
        Func<string, bool>? confirmRename,
        SqlDialect sqlDialect)
    {
        var unmatchedOld = oldTable.Columns.Where(c => !c.IsSystemColumn).ToList();
        var unmatchedNew = newTable.Columns.Where(c => !c.IsSystemColumn).ToList();
        List<(ColumnSchema Old, ColumnSchema New)> matchedPairs = [];

        for (var i = unmatchedOld.Count - 1; i >= 0; i--)
        {
            var o = unmatchedOld[i];
            var n = unmatchedNew.FirstOrDefault(x =>
                string.Equals(x.ColumnName, o.ColumnName, StringComparison.Ordinal));
            if (n is not null)
            {
                matchedPairs.Add((o, n));
                unmatchedOld.RemoveAt(i);
                unmatchedNew.Remove(n);
            }
        }

        for (var i = unmatchedOld.Count - 1; i >= 0; i--)
        {
            var o = unmatchedOld[i];
            var n = unmatchedNew.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(x.PreviousColumnName) &&
                 string.Equals(x.PreviousColumnName, o.ColumnName, StringComparison.Ordinal)) ||
                string.Equals(x.PropertyName, o.PropertyName, StringComparison.Ordinal));

            if (n is not null)
            {
                ops.Add(new RenameColumnOp(newTable.Schema, newTable.TableName, o.ColumnName, n.ColumnName));
                matchedPairs.Add((o, n));
                unmatchedOld.RemoveAt(i);
                unmatchedNew.Remove(n);
            }
        }

        if (confirmRename is not null && unmatchedOld.Count > 0 && unmatchedNew.Count > 0)
        {
            for (var i = unmatchedOld.Count - 1; i >= 0; i--)
            {
                var o = unmatchedOld[i];
                for (var j = 0; j < unmatchedNew.Count; j++)
                {
                    var n = unmatchedNew[j];
                    if (confirmRename($"Table '{newTable.QualifiedName}': Was column '{o.ColumnName}' ({o.SqlType}) renamed to '{n.ColumnName}' ({n.SqlType})?"))
                    {
                        ops.Add(new RenameColumnOp(newTable.Schema, newTable.TableName, o.ColumnName, n.ColumnName));
                        matchedPairs.Add((o, n));
                        unmatchedOld.RemoveAt(i);
                        unmatchedNew.RemoveAt(j);
                        break;
                    }
                }
            }
        }

        foreach (var dropped in unmatchedOld)
        {
            ops.Add(new DropColumnOp(newTable.Schema, newTable.TableName, dropped.ColumnName));
        }

        foreach (var added in unmatchedNew)
        {
            ops.Add(new AddColumnOp(newTable.Schema, newTable.TableName, added));
            if (!string.IsNullOrWhiteSpace(added.Comment))
            {
                ops.Add(new SetColumnCommentOp(newTable.Schema, newTable.TableName, added.ColumnName, added.Comment));
            }
        }

        var supportsComputedStorageMode = MigrationDialectRegistry.Get(sqlDialect).SupportsComputedStorageMode;
        foreach (var (o, n) in matchedPairs)
        {
            var computedStorageChanged = supportsComputedStorageMode
                                         && !string.IsNullOrWhiteSpace(n.ComputedColumnSql)
                                         && o.IsComputedStored != n.IsComputedStored;
            var changed = !string.Equals(o.SqlType, n.SqlType, StringComparison.OrdinalIgnoreCase)
                          || o.IsNullable != n.IsNullable
                          || o.IsIdentity != n.IsIdentity
                          || !string.Equals(o.DefaultValueSql, n.DefaultValueSql, StringComparison.Ordinal)
                          || !string.Equals(o.ComputedColumnSql, n.ComputedColumnSql, StringComparison.Ordinal)
                          || computedStorageChanged;

            if (changed)
            {
                ops.Add(new AlterColumnOp(newTable.Schema, newTable.TableName, o, n));
            }

            if (!string.Equals(o.Comment, n.Comment, StringComparison.Ordinal))
            {
                ops.Add(new SetColumnCommentOp(newTable.Schema, newTable.TableName, n.ColumnName, n.Comment));
            }
        }
    }

    private static List<IndexSchema> DiffIndexesDropAndRename(
        TableSchema oldTable,
        TableSchema newTable,
        List<SchemaOperation> ops)
    {
        var oldList = new List<IndexSchema>(oldTable.Indexes);
        var newList = new List<IndexSchema>(newTable.Indexes);

        for (var i = oldList.Count - 1; i >= 0; i--)
        {
            var o = oldList[i];
            var exact = newList.FirstOrDefault(n =>
                string.Equals(o.Name, n.Name, StringComparison.Ordinal) &&
                IndexDefinitionEquals(o, n));

            if (exact is not null)
            {
                if (!string.Equals(o.Tablespace, exact.Tablespace, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(exact.Tablespace))
                {
                    ops.Add(new SetIndexTablespaceOp(newTable.Schema, exact.Name, exact.Tablespace));
                }

                DiffIndexStorageParameters(newTable.Schema, exact.Name, o.StorageParameters, exact.StorageParameters, ops);

                oldList.RemoveAt(i);
                newList.Remove(exact);
            }
        }

        for (var i = oldList.Count - 1; i >= 0; i--)
        {
            var o = oldList[i];
            var renamed = newList.FirstOrDefault(n => IndexDefinitionEquals(o, n));

            if (renamed is not null)
            {
                ops.Add(new RenameIndexOp(newTable.Schema, newTable.TableName, o.Name, renamed.Name));
                if (!string.Equals(o.Tablespace, renamed.Tablespace, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(renamed.Tablespace))
                {
                    ops.Add(new SetIndexTablespaceOp(newTable.Schema, renamed.Name, renamed.Tablespace));
                }

                DiffIndexStorageParameters(newTable.Schema, renamed.Name, o.StorageParameters, renamed.StorageParameters, ops);

                oldList.RemoveAt(i);
                newList.Remove(renamed);
            }
        }

        foreach (var dropped in oldList)
        {
            ops.Add(new DropIndexOp(newTable.Schema, newTable.TableName, dropped.Name));
        }

        return newList;
    }

    private static void DiffIndexStorageParameters(
        string? schema,
        string indexName,
        IReadOnlyDictionary<string, string>? oldParamsInput,
        IReadOnlyDictionary<string, string>? newParamsInput,
        List<SchemaOperation> ops)
    {
        var oldParams = oldParamsInput ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newParams = newParamsInput ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var toSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, val) in newParams.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!oldParams.TryGetValue(key, out var oldVal) || !string.Equals(oldVal, val, StringComparison.Ordinal))
            {
                toSet[key] = val;
            }
        }

        var toReset = oldParams.Keys
            .Where(k => !newParams.ContainsKey(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (toSet.Count > 0 || toReset.Count > 0)
        {
            ops.Add(new AlterIndexStorageParametersOp(schema, indexName, toSet, toReset));
        }
    }

    private static bool ExclusionConstraintEquals(ExclusionConstraintSchema a, ExclusionConstraintSchema b)
    {
        if (!string.Equals(a.Method, b.Method, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.FilterSql, b.FilterSql, StringComparison.Ordinal)
            || !string.Equals(a.Tablespace, b.Tablespace, StringComparison.Ordinal)
            || !NullableSequenceEqual(a.IncludedColumnNames, b.IncludedColumnNames)
            || !NullableDictionaryEqual(a.StorageParameters, b.StorageParameters)
            || a.Elements.Count != b.Elements.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Elements.Count; i++)
        {
            var ea = a.Elements[i];
            var eb = b.Elements[i];
            if (!string.Equals(ea.ColumnName, eb.ColumnName, StringComparison.Ordinal)
                || !string.Equals(ea.ExpressionSql, eb.ExpressionSql, StringComparison.Ordinal)
                || !string.Equals(ea.OperatorClass, eb.OperatorClass, StringComparison.Ordinal)
                || !string.Equals(ea.Operator, eb.Operator, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IndexDefinitionEquals(IndexSchema a, IndexSchema b)
    {
        return a.IsUnique == b.IsUnique
               && string.Equals(a.FilterSql, b.FilterSql, StringComparison.Ordinal)
               && string.Equals(a.Method, b.Method, StringComparison.OrdinalIgnoreCase)
               && a.ColumnNames.SequenceEqual(b.ColumnNames, StringComparer.Ordinal)
               && NullableSequenceEqual(a.IncludedColumnNames, b.IncludedColumnNames)
               && NullableBoolSequenceEqual(a.IsDescending, b.IsDescending)
               && NullableDictionaryEqual(a.OperatorsByColumn, b.OperatorsByColumn);
    }

    private static bool NullableDictionaryEqual(
        IReadOnlyDictionary<string, string>? a,
        IReadOnlyDictionary<string, string>? b)
    {
        if (a is null or { Count: 0 } && b is null or { Count: 0 })
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, val) in a)
        {
            if (!b.TryGetValue(key, out var otherVal) ||
                !string.Equals(val, otherVal, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NullableSequenceEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        (a is null or { Count: 0 } && b is null or { Count: 0 }) ||
        (a is not null && b is not null && a.SequenceEqual(b, StringComparer.Ordinal));

    private static bool NullableBoolSequenceEqual(IReadOnlyList<bool>? a, IReadOnlyList<bool>? b) =>
        (a is null or { Count: 0 } && b is null or { Count: 0 }) ||
        (a is not null && b is not null && a.SequenceEqual(b));

    private static bool ForeignKeyEquals(
        ForeignKeySchema a,
        ForeignKeySchema b,
        IReadOnlyDictionary<string, string> renamedTableMap)
    {
        var aPrincipal = NormalizeQualifiedPrincipal(a.PrincipalSchema, a.PrincipalTable);
        var bPrincipal = NormalizeQualifiedPrincipal(b.PrincipalSchema, b.PrincipalTable);
        var aRaw = string.IsNullOrWhiteSpace(a.PrincipalSchema)
            ? a.PrincipalTable
            : $"{a.PrincipalSchema}.{a.PrincipalTable}";
        var bRaw = string.IsNullOrWhiteSpace(b.PrincipalSchema)
            ? b.PrincipalTable
            : $"{b.PrincipalSchema}.{b.PrincipalTable}";

        var principalMatches = string.Equals(aPrincipal, bPrincipal, StringComparison.Ordinal)
                               || (renamedTableMap.TryGetValue(aRaw, out var mapped) &&
                                   string.Equals(mapped, bRaw, StringComparison.Ordinal));

        return principalMatches
               && a.OnDelete == b.OnDelete
               && a.DependentColumns.SequenceEqual(b.DependentColumns, StringComparer.Ordinal)
               && a.PrincipalColumns.SequenceEqual(b.PrincipalColumns, StringComparer.Ordinal);
    }

    private static string NormalizeQualifiedPrincipal(string? schema, string table)
    {
        if (string.IsNullOrWhiteSpace(schema) || string.Equals(schema.Trim(), "public", StringComparison.Ordinal))
        {
            return table;
        }

        return $"{schema.Trim()}.{table}";
    }
}
