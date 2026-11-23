using Microsoft.Extensions.DependencyInjection;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Configuration;

public static class MigrationManagerServiceCollectionExtensions
{
    public static IServiceCollection AddDbMigrator(
        this IServiceCollection services,
        Action<MigrationManagerOptionsBuilder> configure) =>
        services.AddDbMigrator((_, builder) => configure(builder));

    public static IServiceCollection AddDbMigrator(
        this IServiceCollection services,
        Action<IServiceProvider, MigrationManagerOptionsBuilder> configure)
    {
        services.AddSingleton(sp =>
        {
            var builder = new MigrationManagerOptionsBuilder();
            configure(sp, builder);
            return builder.Build();
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<MigrationManagerOptions>();
            var schema = SchemaScanner.ScanRegistries(
                options.Dialect,
                options.IdentifierCase,
                options.PropertyConventions,
                options.Enums,
                [.. options.Registries]);
            schema.Extensions = options.Extensions.Count > 0 ? [.. options.Extensions] : null;
            schema.Enums = options.Enums.Count > 0 ? [.. options.Enums] : null;
            return schema;
        });

        services.AddSingleton<IMigrationManager, MigrationManager>();

        return services;
    }
}
