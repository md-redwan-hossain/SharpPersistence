using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli.Ui;

internal static class UiDryRunBuilder
{
    public static UiDryRunDto Build(
        IReadOnlyList<(string RegistryName, DatabaseModel Model)> scannedRegistries,
        DbMigratorOptions options)
    {
        var dialect = MigrationDialectRegistry.Get(options);
        var stateDir = new MigrationScriptStore(options).ResolveSnapshotDirectory();

        var items = new List<UiDryRunRegistryDto>(scannedRegistries.Count + 1);
        var totalAdds = 0;
        var totalAlters = 0;
        var totalDrops = 0;

        if (dialect.SupportsGlobalExtensionsAndEnums &&
            (options.PostgreSql.Extensions.Count > 0 || options.PostgreSql.Enums.Count > 0))
        {
            var globalSnapshotPath = Path.Combine(stateDir, "_global.json");
            var prevGlobalJson = File.Exists(globalSnapshotPath) ? File.ReadAllText(globalSnapshotPath) : null;
            var globalModel = new DatabaseModel
            {
                Extensions = options.PostgreSql.Extensions.Count > 0 ? [.. options.PostgreSql.Extensions] : null,
                Enums = options.PostgreSql.Enums.Count > 0 ? [.. options.PostgreSql.Enums] : null
            };
            var globalDiff = StandaloneSchemaDiffer.GenerateDiff(
                globalModel,
                prevGlobalJson,
                options.Dialect,
                interactiveRenamePrompt: false,
                options.Oracle.Compatibility);

            var globalItem = MapRegistry("_global", globalDiff);
            if (globalItem.HasChanges)
            {
                items.Add(globalItem);
                totalAdds += globalItem.AddCount;
                totalAlters += globalItem.AlterCount;
                totalDrops += globalItem.DropCount;
            }
        }

        foreach (var (registryName, model) in scannedRegistries)
        {
            var snapshotPath = Path.Combine(stateDir, $"{registryName}.json");
            var prevJson = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : null;

            var diff = StandaloneSchemaDiffer.GenerateDiff(
                model,
                prevJson,
                options.Dialect,
                interactiveRenamePrompt: false,
                options.Oracle.Compatibility);

            var regItem = MapRegistry(registryName, diff);
            items.Add(regItem);
            totalAdds += regItem.AddCount;
            totalAlters += regItem.AlterCount;
            totalDrops += regItem.DropCount;
        }

        return new UiDryRunDto(
            ChangedRegistryCount: items.Count(static i => i.HasChanges),
            AddCount: totalAdds,
            AlterCount: totalAlters,
            DropCount: totalDrops,
            Registries: items);
    }

    private static UiDryRunRegistryDto MapRegistry(string registryName, SchemaDiffResult diff)
    {
        var ops = (diff.Operations ?? [])
            .Select(MapOperation)
            .ToList();

        var addCount = ops.Count(static o => o.Kind == "Add");
        var alterCount = ops.Count(static o => o.Kind == "Alter");
        var dropCount = ops.Count(static o => o.Kind == "Drop");

        return new UiDryRunRegistryDto(
            RegistryName: registryName,
            HasChanges: diff.HasChanges,
            HasDestructiveChanges: dropCount > 0,
            AddCount: addCount,
            AlterCount: alterCount,
            DropCount: dropCount,
            Operations: ops,
            SqlScript: diff.SqlScript,
            DeferredSqlScript: diff.DeferredSqlScript);
    }

    private static UiDryRunOperationDto MapOperation(SchemaOperation op) => op switch
    {
        EnsureExtensionOp e => new("Add", "Extension", e.Name, $"Ensure extension {e.Name}"),
        DropExtensionOp e => new("Drop", "Extension", e.Name, $"Drop extension {e.Name}"),
        CreateEnumOp e => new("Add", "Enum", Qualify(e.Enum.Schema, e.Enum.Name), $"Create enum ({string.Join(", ", e.Enum.Labels)})"),
        AddEnumValueOp e => new("Alter", "Enum", Qualify(e.Schema, e.EnumName), $"Add enum value '{e.Value}'"),
        DropEnumOp e => new("Drop", "Enum", Qualify(e.Schema, e.Name), $"Drop enum {e.Name}"),
        EnsureSchemaOp s => new("Add", "Schema", s.SchemaName, $"Ensure schema {s.SchemaName}"),
        CreateSequenceOp s => new("Add", "Sequence", Qualify(s.Sequence.Schema, s.Sequence.Name), $"Create sequence (start {s.Sequence.StartValue}, inc {s.Sequence.IncrementBy})"),
        AlterSequenceOp s => new("Alter", "Sequence", Qualify(s.NewSeq.Schema, s.NewSeq.Name), $"Alter sequence {s.NewSeq.Name}"),
        DropSequenceOp s => new("Drop", "Sequence", Qualify(s.Schema, s.Name), $"Drop sequence {s.Name}"),
        CreateTableOp t => new("Add", "Table", t.Table.QualifiedName, $"Create table ({t.Table.Columns.Count} columns)"),
        DropTableOp t => new("Drop", "Table", Qualify(t.Schema, t.TableName), "Drop table"),
        RenameTableOp t => new("Alter", "Table", Qualify(t.NewSchema, t.NewTableName), $"Rename {Qualify(t.OldSchema, t.OldTableName)} -> {Qualify(t.NewSchema, t.NewTableName)}"),
        AddColumnOp c => new("Add", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.Column.ColumnName}", $"{c.Column.SqlType} {(c.Column.IsNullable ? "NULL" : "NOT NULL")}"),
        DropColumnOp c => new("Drop", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.ColumnName}", "Drop column"),
        RenameColumnOp c => new("Alter", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.NewName}", $"Rename {c.OldName} -> {c.NewName}"),
        AlterColumnOp c => new("Alter", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.NewCol.ColumnName}", $"{c.OldCol.SqlType} {(c.OldCol.IsNullable ? "NULL" : "NOT NULL")} -> {c.NewCol.SqlType} {(c.NewCol.IsNullable ? "NULL" : "NOT NULL")}"),
        AddPrimaryKeyOp pk => new("Add", "PrimaryKey", Qualify(pk.Schema, pk.TableName), $"{pk.PrimaryKey.Name} ({string.Join(", ", pk.PrimaryKey.ColumnNames)})"),
        DropPrimaryKeyOp pk => new("Drop", "PrimaryKey", Qualify(pk.Schema, pk.TableName), $"Drop PK {pk.Name}"),
        AddForeignKeyOp fk => new("Add", "ForeignKey", Qualify(fk.Schema, fk.TableName), $"{fk.ForeignKey.Name} -> {Qualify(fk.ForeignKey.PrincipalSchema, fk.ForeignKey.PrincipalTable)}{(fk.ForeignKey.IsCircular ? " (deferred)" : string.Empty)}"),
        DropForeignKeyOp fk => new("Drop", "ForeignKey", Qualify(fk.Schema, fk.TableName), $"Drop FK {fk.Name}"),
        CreateIndexOp ix => new("Add", "Index", Qualify(ix.Schema, ix.TableName), $"{ix.Index.Name} ({string.Join(", ", ix.Index.ColumnNames)})"),
        DropIndexOp ix => new("Drop", "Index", Qualify(ix.Schema, ix.TableName), $"Drop index {ix.Name}"),
        RenameIndexOp ix => new("Alter", "Index", Qualify(ix.Schema, ix.TableName), $"Rename index {ix.OldName} -> {ix.NewName}"),
        AddCheckConstraintOp ck => new("Add", "Check", Qualify(ck.Schema, ck.TableName), $"{ck.Constraint.Name}: {ck.Constraint.Sql}"),
        DropCheckConstraintOp ck => new("Drop", "Check", Qualify(ck.Schema, ck.TableName), $"Drop check {ck.Name}"),
        AddExclusionConstraintOp ex => new("Add", "Exclusion", Qualify(ex.Schema, ex.TableName), $"{ex.Constraint.Name} (USING {ex.Constraint.Method})"),
        DropExclusionConstraintOp ex => new("Drop", "Exclusion", Qualify(ex.Schema, ex.TableName), $"Drop exclusion {ex.Name}"),
        RenameConstraintOp rc => new("Alter", "Constraint", Qualify(rc.Schema, rc.TableName), $"Rename constraint {rc.OldName} -> {rc.NewName}"),
        SetTableCommentOp tc => new("Alter", "Comment", Qualify(tc.Schema, tc.TableName), string.IsNullOrEmpty(tc.Comment) ? "Clear table comment" : "Update table comment"),
        SetColumnCommentOp cc => new("Alter", "Comment", $"{Qualify(cc.Schema, cc.TableName)}.{cc.ColumnName}", string.IsNullOrEmpty(cc.Comment) ? "Clear column comment" : "Update column comment"),
        SetTableLoggedOp tl => new("Alter", "Storage", Qualify(tl.Schema, tl.TableName), tl.IsUnlogged ? "Set UNLOGGED" : "Set LOGGED"),
        SetTableTablespaceOp tt => new("Alter", "Storage", Qualify(tt.Schema, tt.TableName), $"Set tablespace {tt.Tablespace}"),
        AlterTableStorageParametersOp sp => new("Alter", "Storage", Qualify(sp.Schema, sp.TableName), "Alter table storage parameters"),
        SetIndexTablespaceOp it => new("Alter", "Storage", Qualify(it.Schema, it.IndexName), $"Set index tablespace {it.Tablespace}"),
        AlterIndexStorageParametersOp isp => new("Alter", "Storage", Qualify(isp.Schema, isp.IndexName), "Alter index storage parameters"),
        _ => new("Alter", "Schema", op.GetType().Name, op.GetType().Name)
    };

    private static string Qualify(string? schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";
}
