using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Configuration;

public static class DbMigratorServiceCollectionExtensions
{
    public static IServiceCollection AddDbMigrator(
        this IServiceCollection services,
        Action<DbMigratorOptionsBuilder> configure) =>
        services.AddDbMigrator((_, builder) => configure(builder));

    public static IServiceCollection AddDbMigrator(
        this IServiceCollection services,
        Action<IServiceProvider, DbMigratorOptionsBuilder> configure)
    {
        services.AddSingleton(sp =>
        {
            var builder = new DbMigratorOptionsBuilder();
            configure(sp, builder);
            return builder.Build();
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<DbMigratorOptions>();
            var schema = SchemaScanner.ScanRegistries(
                options.Dialect,
                options.IdentifierCase,
                options.PropertyConventions,
                options.PostgreSql.Enums,
                options.SqlExpressionOptions,
                options.PluralizeTableNames,
                options.EntityConventions,
                options.TableNameConvention,
                options.EntityReplacements,
                [.. options.Registries]);
            schema.Extensions = options.PostgreSql.Extensions.Count > 0 ? [.. options.PostgreSql.Extensions] : null;
            schema.Enums = options.PostgreSql.Enums.Count > 0 ? [.. options.PostgreSql.Enums] : null;
            return schema;
        });

        services.AddSingleton<IDbMigrator, DbMigrator>();

        return services;
    }
}
