using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Configuration;

internal sealed record PropertyConventionRule(Type TargetClrType, Action<ColumnBuilder> Configure);

internal sealed record EntityConventionRule(Type TargetType, Action<TableDefinition> Apply);

internal sealed class MigrationManagerOptions
{
    public SqlDialect Dialect { get; internal set; }
    public SqlIdentifierCase IdentifierCase { get; internal set; }
    public bool PluralizeTableNames { get; internal set; }
    public Func<Type, string, string>? TableNameConvention { get; internal set; }
    public OracleDbCompatibility OracleCompatibility { get; internal set; } = OracleDbCompatibility.Oracle19;
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
    public IReadOnlyList<EntityConventionRule> EntityConventions { get; internal set; } = [];
    public IReadOnlyDictionary<Type, Type> EntityStubs { get; internal set; } = new Dictionary<Type, Type>();
    public IReadOnlyList<PostgresExtensionSchema> Extensions { get; internal set; } = [];
    public IReadOnlyList<PostgresEnumSchema> Enums { get; internal set; } = [];
    public SqlExpressionBuilderOptions SqlExpressionOptions { get; } = new();

    public void OverrideRootPath(string rootPath) => RootPath = rootPath;
}
