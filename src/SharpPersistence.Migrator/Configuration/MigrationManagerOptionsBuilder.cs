using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Configuration;

public class MigrationManagerOptionsBuilder
{
    internal sealed class BuilderState
    {
        public MigrationManagerOptions Options { get; } = new();
        public List<IEntityRegistry> Registries { get; } = [];
        public List<PropertyConventionRule> PropertyConventions { get; } = [];
        public List<EntityConventionRule> EntityConventions { get; } = [];
        public Dictionary<Type, Type> EntityStubs { get; } = [];
        public List<PostgresExtensionSchema> Extensions { get; } = [];
        public List<(Type EnumType, string? Name, string? Schema)> EnumRegistrations { get; } = [];
        public bool DialectConfigured { get; set; }
        public bool IdentifierCaseExplicitlySet { get; set; }
        public bool HistorySchemaExplicitlySet { get; set; }
    }

    internal BuilderState State { get; }

    public MigrationManagerOptionsBuilder()
    {
        State = new BuilderState();
    }

    internal MigrationManagerOptionsBuilder(BuilderState state)
    {
        State = state;
    }

    public PostgreSqlOptionsBuilder UsePostgreSql(
        string connectionString,
        DbProviderFactory? providerFactory = null)
    {
        State.Options.ConnectionString = connectionString;
        State.Options.ProviderFactory = providerFactory;
        return ConfigurePostgreSqlDefaults();
    }

    public PostgreSqlOptionsBuilder UsePostgreSql(Func<IServiceProvider, DbConnection> connectionFactory)
    {
        State.Options.ConnectionFactory = connectionFactory;
        return ConfigurePostgreSqlDefaults();
    }

    public OracleOptionsBuilder UseOracle(
        string connectionString,
        DbProviderFactory? providerFactory = null)
    {
        State.Options.ConnectionString = connectionString;
        State.Options.ProviderFactory = providerFactory;
        return ConfigureOracleDefaults();
    }

    public OracleOptionsBuilder UseOracle(Func<IServiceProvider, DbConnection> connectionFactory)
    {
        State.Options.ConnectionFactory = connectionFactory;
        return ConfigureOracleDefaults();
    }

    private PostgreSqlOptionsBuilder ConfigurePostgreSqlDefaults()
    {
        State.Options.Dialect = SqlDialect.PostgreSql;
        State.DialectConfigured = true;
        if (!State.IdentifierCaseExplicitlySet)
        {
            State.Options.IdentifierCase = SqlIdentifierCase.LowerSnakeCase;
        }

        if (!State.HistorySchemaExplicitlySet)
        {
            State.Options.HistorySchema = "public";
        }

        return new PostgreSqlOptionsBuilder(State);
    }

    private OracleOptionsBuilder ConfigureOracleDefaults()
    {
        State.Options.Dialect = SqlDialect.Oracle;
        State.DialectConfigured = true;
        if (!State.IdentifierCaseExplicitlySet)
        {
            State.Options.IdentifierCase = SqlIdentifierCase.UpperSnakeCase;
        }

        return new OracleOptionsBuilder(State);
    }

    public MigrationManagerOptionsBuilder UseNamingConvention(SqlIdentifierCase namingConvention)
    {
        State.Options.IdentifierCase = namingConvention;
        State.IdentifierCaseExplicitlySet = true;
        return this;
    }

    public MigrationManagerOptionsBuilder PluralizeTableNames(bool pluralize = true)
    {
        State.Options.PluralizeTableNames = pluralize;
        return this;
    }

    public MigrationManagerOptionsBuilder UseTableNameConvention(Func<Type, string, string> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        State.Options.TableNameConvention = convention;
        return this;
    }

    public MigrationManagerOptionsBuilder UseHistoryTable(string tableName, string? schema = null)
    {
        State.Options.HistoryTableName = tableName;
        State.Options.HistorySchema = schema;
        State.HistorySchemaExplicitlySet = true;
        return this;
    }

    public MigrationManagerOptionsBuilder UseMigrationsDirectory(string migrationsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);
        State.Options.MigrationsDirectory = migrationsDirectory;
        return this;
    }

    public MigrationManagerOptionsBuilder UseSnapshotDirectory(string snapshotDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotDirectory);
        State.Options.SnapshotDirectory = snapshotDirectory;
        return this;
    }

    public MigrationManagerOptionsBuilder UseRootPath(string rootPath)
    {
        State.Options.RootPath = rootPath;
        return this;
    }

    public MigrationManagerOptionsBuilder ConfigureProperties<TProperty>(Action<ColumnBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        State.PropertyConventions.Add(new PropertyConventionRule(typeof(TProperty), configure));
        return this;
    }

    public MigrationManagerOptionsBuilder ConfigureEntities<TEntity>(Action<EntityBuilder<TEntity>> configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        State.EntityConventions.Add(new EntityConventionRule(
            typeof(TEntity),
            tableDef => configure(new EntityBuilder<TEntity>(tableDef))));
        return this;
    }

    public MigrationManagerOptionsBuilder ConfigureEntityStubs(Action<EntityStubBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new EntityStubBuilder(State.EntityStubs));
        return this;
    }

    public MigrationManagerOptionsBuilder ConfigureSqlExpressionBuilder(
        Action<SqlExpressionBuilderOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new SqlExpressionBuilderOptionsBuilder(State.Options.SqlExpressionOptions));
        return this;
    }

    public MigrationManagerOptionsBuilder AddRegistry<TRegistry>()
        where TRegistry : class, IEntityRegistry, new()
    {
        State.Registries.Add(new TRegistry());
        return this;
    }

    public MigrationManagerOptionsBuilder AddRegistry(IEntityRegistry registry)
    {
        State.Registries.Add(registry);
        return this;
    }

    internal MigrationManagerOptions Build()
    {
        if (!State.DialectConfigured)
        {
            throw new InvalidOperationException(
                "No database provider configured. Call a provider method such as UsePostgreSql(...) or UseOracle(...) inside AddDbMigrator.");
        }

        var identifierCase = State.Options.IdentifierCase;

        State.Options.Registries = State.Registries
            .GroupBy(r => r.GetType())
            .Select(g => g.First())
            .ToList();
        State.Options.PropertyConventions = [.. State.PropertyConventions];
        State.Options.EntityConventions = [.. State.EntityConventions];
        State.Options.EntityStubs = new Dictionary<Type, Type>(State.EntityStubs);
        State.Options.Extensions = State.Extensions
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .Select(g => g.Last())
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
        State.Options.Enums = State.EnumRegistrations
            .GroupBy(e => e.EnumType)
            .Select(g => g.Last())
            .Select(e => new PostgresEnumSchema
            {
                Name = e.Name ?? NamingFormatter.Format(e.EnumType.Name, identifierCase),
                Schema = e.Schema,
                ClrTypeName = e.EnumType.FullName ?? e.EnumType.Name,
                Labels = Enum.GetNames(e.EnumType)
                    .Select(l => NamingFormatter.Format(l, identifierCase))
                    .ToList()
            })
            .OrderBy(e => e.QualifiedName, StringComparer.Ordinal)
            .ToList();
        return State.Options;
    }
}

public sealed class PostgreSqlOptionsBuilder : MigrationManagerOptionsBuilder
{
    internal PostgreSqlOptionsBuilder(BuilderState state) : base(state)
    {
    }

    public PostgreSqlOptionsBuilder HasPostgresExtension(string name, string? schema = null)
    {
        State.Extensions.RemoveAll(e => string.Equals(e.Name, name, StringComparison.Ordinal));
        State.Extensions.Add(new PostgresExtensionSchema(name, schema));
        return this;
    }

    public PostgreSqlOptionsBuilder HasPostgresEnum<TEnum>(string? name = null, string? schema = null)
        where TEnum : struct, Enum
    {
        State.EnumRegistrations.RemoveAll(e => e.EnumType == typeof(TEnum));
        State.EnumRegistrations.Add((typeof(TEnum), name, schema));
        return this;
    }
}

public sealed class OracleOptionsBuilder : MigrationManagerOptionsBuilder
{
    internal OracleOptionsBuilder(BuilderState state) : base(state)
    {
    }

    public OracleOptionsBuilder UseSqlCompatibility(OracleDbCompatibility compatibility)
    {
        State.Options.OracleCompatibility = compatibility;
        return this;
    }
}
