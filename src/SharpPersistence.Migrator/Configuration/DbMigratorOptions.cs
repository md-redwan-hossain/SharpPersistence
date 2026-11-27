using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Configuration;

internal sealed record PropertyConventionRule(Type TargetClrType, Action<ColumnBuilder> Configure);

internal sealed record EntityConventionRule(Type TargetType, Action<TableDefinition> Apply);

internal sealed class DbMigratorOptions
{
    public SqlDialect Dialect { get; internal set; }
    public SqlIdentifierCase IdentifierCase { get; internal set; }
    public bool PluralizeTableNames { get; internal set; }
    public Func<Type, string, string>? TableNameConvention { get; internal set; }
    public OracleDbCompatibility OracleCompatibility { get; internal set; } = OracleDbCompatibility.Oracle19;
    public string? ConnectionString { get; internal set; }
    public DbProviderFactory? ProviderFactory { get; internal set; }
    public Func<IServiceProvider, DbConnection>? ConnectionFactory { get; internal set; }
    public string HistoryTableName { get; internal set; } = "__db_migrator_logs";
    public string? HistorySchema { get; internal set; }
    public string? RootPath { get; internal set; }
    public const string DefaultBaseDirectory = "DbMigrator";
    public static readonly string DefaultMigrationsDirectory = Path.Combine(DefaultBaseDirectory, "Scripts");
    public static readonly string DefaultSnapshotDirectory = Path.Combine(DefaultBaseDirectory, "Snapshots");

    public string MigrationsDirectory => DefaultMigrationsDirectory;
    public string SnapshotDirectory => DefaultSnapshotDirectory;
    public IReadOnlyList<IEntityRegistry> Registries { get; internal set; } = [];
    public IReadOnlyList<PropertyConventionRule> PropertyConventions { get; internal set; } = [];
    public IReadOnlyList<EntityConventionRule> EntityConventions { get; internal set; } = [];
    public IReadOnlyDictionary<Type, Type> EntityReplacements { get; internal set; } = new Dictionary<Type, Type>();
    public IReadOnlyList<PostgresExtensionSchema> Extensions { get; internal set; } = [];
    public IReadOnlyList<PostgresEnumSchema> Enums { get; internal set; } = [];
    public SqlExpressionBuilderOptions SqlExpressionOptions { get; } = new();

    public void OverrideRootPath(string rootPath) => RootPath = rootPath;
}
