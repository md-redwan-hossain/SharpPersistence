using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Configuration;

internal sealed record PropertyConventionRule(Type TargetClrType, Action<ColumnBuilder> Configure);

internal sealed record EntityConventionRule(ModelCreationPhase Phase, Action<TableDefinition> Apply);

internal sealed class DbMigratorOptions
{
    public SqlDialect Dialect { get; internal set; }
    public SqlIdentifierCase IdentifierCase { get; internal set; }
    public bool PluralizeTableNames { get; internal set; }
    public Func<Type, string, string>? TableNameConvention { get; internal set; }
    public string? ConnectionString { get; internal set; }
    public DbProviderFactory? ProviderFactory { get; internal set; }
    public Func<IServiceProvider, DbConnection>? ConnectionFactory { get; internal set; }
    public string? HistorySchema { get; internal set; }
    public string? RootPath { get; internal set; }
    public IReadOnlyList<IEntityRegistry> Registries { get; internal set; } = [];
    public IReadOnlyList<PropertyConventionRule> PropertyConventions { get; internal set; } = [];
    public IReadOnlyList<EntityConventionRule> EntityConventions { get; internal set; } = [];
    public IReadOnlyDictionary<Type, Type> EntityReplacements { get; internal set; } = new Dictionary<Type, Type>();
    public SqlExpressionBuilderOptions SqlExpressionOptions { get; } = new();

    public PostgresMigratorOptions PostgreSql { get; } = new();
    public OracleMigratorOptions Oracle { get; } = new();

    public void OverrideRootPath(string rootPath) => RootPath = rootPath;
}
