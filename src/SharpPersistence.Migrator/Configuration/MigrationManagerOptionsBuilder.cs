using System.Data.Common;
using System.Reflection;
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
        State.Options.Dialect = SqlDialect.PostgreSql;
        State.Options.ConnectionString = connectionString;
        State.Options.ProviderFactory = providerFactory;
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

    public PostgreSqlOptionsBuilder UsePostgreSql(Func<IServiceProvider, DbConnection> connectionFactory)
    {
        State.Options.Dialect = SqlDialect.PostgreSql;
        State.Options.ConnectionFactory = connectionFactory;
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

    public OracleOptionsBuilder UseOracle(
        string connectionString,
        DbProviderFactory? providerFactory = null)
    {
        State.Options.Dialect = SqlDialect.Oracle;
        State.Options.ConnectionString = connectionString;
        State.Options.ProviderFactory = providerFactory;
        State.DialectConfigured = true;
        if (!State.IdentifierCaseExplicitlySet)
        {
            State.Options.IdentifierCase = SqlIdentifierCase.UpperSnakeCase;
        }

        return new OracleOptionsBuilder(State);
    }

    public OracleOptionsBuilder UseOracle(Func<IServiceProvider, DbConnection> connectionFactory)
    {
        State.Options.Dialect = SqlDialect.Oracle;
        State.Options.ConnectionFactory = connectionFactory;
        State.DialectConfigured = true;
        if (!State.IdentifierCaseExplicitlySet)
        {
            State.Options.IdentifierCase = SqlIdentifierCase.UpperSnakeCase;
        }

        return new OracleOptionsBuilder(State);
    }

    public MigrationManagerOptionsBuilder UseIdentifierCase(SqlIdentifierCase identifierCase)
    {
        State.Options.IdentifierCase = identifierCase;
        State.IdentifierCaseExplicitlySet = true;
        return this;
    }

    public MigrationManagerOptionsBuilder UseHistoryTable(string tableName, string? schema = null)
    {
        State.Options.HistoryTableName = tableName;
        State.Options.HistorySchema = schema;
        State.HistorySchemaExplicitlySet = true;
        return this;
    }

    public MigrationManagerOptionsBuilder UsePaths(string migrationsDirectory, string snapshotDirectory)
    {
        State.Options.MigrationsDirectory = migrationsDirectory;
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
        State.PropertyConventions.Add(new PropertyConventionRule(typeof(TProperty), configure));
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

    public MigrationManagerOptionsBuilder AddRegistriesFromAssembly(params Assembly[] assemblies)
    {
        var discovered = SchemaScanner.DiscoverRegistries(assemblies);
        State.Registries.AddRange(discovered);
        return this;
    }

    internal MigrationManagerOptions Build()
    {
        if (!State.DialectConfigured)
        {
            throw new InvalidOperationException(
                "No database provider configured. Call a provider method such as UsePostgreSql(...) or UseOracle(...) inside AddDbMigrator.");
        }

        State.Options.Registries = State.Registries
            .GroupBy(r => r.GetType())
            .Select(g => g.First())
            .ToList();
        State.Options.PropertyConventions = [.. State.PropertyConventions];
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
                Name = e.Name ?? NamingFormatter.Format(e.EnumType.Name, SqlIdentifierCase.LowerSnakeCase),
                Schema = e.Schema,
                ClrTypeName = e.EnumType.FullName ?? e.EnumType.Name,
                Labels = Enum.GetNames(e.EnumType)
                    .Select(l => NamingFormatter.Format(l, SqlIdentifierCase.LowerSnakeCase))
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

    public PostgreSqlOptionsBuilder HasExtension(string name, string? schema = null)
    {
        State.Extensions.RemoveAll(e => string.Equals(e.Name, name, StringComparison.Ordinal));
        State.Extensions.Add(new PostgresExtensionSchema(name, schema));
        return this;
    }

    public PostgreSqlOptionsBuilder HasEnum<TEnum>(string? name = null, string? schema = null)
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
