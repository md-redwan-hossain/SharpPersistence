using System.Data;
using System.Data.Common;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationConnectionOpener
{
    private readonly DbMigratorOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMigrationDialect _dialect;

    public MigrationConnectionOpener(
        DbMigratorOptions options,
        IServiceProvider serviceProvider,
        IMigrationDialect dialect)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _dialect = dialect;
    }

    public bool CanResolveProviderFactory() =>
        (_options.ProviderFactory ?? _dialect.ResolveDefaultProviderFactory()) is not null;

    public async Task<(DbConnection Connection, bool OwnsConnection)> OpenAsync(CancellationToken ct)
    {
        DbConnection conn;
        bool ownsConnection;

        if (_options.ConnectionFactory is not null)
        {
            conn = _options.ConnectionFactory(_serviceProvider);
            ownsConnection = false;
        }
        else if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            var factory = _options.ProviderFactory
                          ?? _dialect.ResolveDefaultProviderFactory()
                          ?? throw new InvalidOperationException(
                              $"No DbProviderFactory configured and default factory for '{_options.Dialect}' could not be loaded. " +
                              "Install the provider package (e.g. Npgsql or Oracle.ManagedDataAccess.Core) or pass DbProviderFactory / connectionFactory.");

            conn = factory.CreateConnection()
                   ?? throw new InvalidOperationException("DbProviderFactory.CreateConnection() returned null.");
            conn.ConnectionString = _options.ConnectionString;
            ownsConnection = true;
        }
        else
        {
            throw new InvalidOperationException(
                "No database connection configured. Pass a connection string or connectionFactory inside AddDbMigrator.");
        }

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        return (conn, ownsConnection);
    }
}
