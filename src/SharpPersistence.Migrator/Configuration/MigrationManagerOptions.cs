using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Configuration;

internal sealed record PropertyConventionRule(Type TargetClrType, Action<ColumnBuilder> Configure);

internal sealed class MigrationManagerOptions
{
    public SqlDialect Dialect { get; internal set; }
    public SqlIdentifierCase IdentifierCase { get; internal set; }
    public OracleDbCompatibility OracleCompatibility { get; internal set; } = OracleDbCompatibility.OracleDb19;
    public string? ConnectionString { get; internal set; }
    public DbProviderFactory? ProviderFactory { get; internal set; }
    public Func<IServiceProvider, DbConnection>? ConnectionFactory { get; internal set; }
    public string HistoryTableName { get; internal set; } = "__db_migration_logs";
    public string? HistorySchema { get; internal set; }
    public string? RootPath { get; internal set; }
    public string MigrationsDirectory { get; internal set; } = "MigrationScripts";
    public string SnapshotDirectory { get; internal set; } = "EntityChangelogs";
    public IReadOnlyList<IEntityRegistry> Registries { get; internal set; } = [];
    public IReadOnlyList<PropertyConventionRule> PropertyConventions { get; internal set; } = [];
    public IReadOnlyList<PostgresExtensionSchema> Extensions { get; internal set; } = [];
    public IReadOnlyList<PostgresEnumSchema> Enums { get; internal set; } = [];
    public CheckConstraintBuilderOptions CheckConstraintOptions { get; } = new();

    public void OverrideRootPath(string rootPath) => RootPath = rootPath;
}
