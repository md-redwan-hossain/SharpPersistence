using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpPersistence.Migrator.Builders;

namespace SharpPersistence.Migrator.EfCore;

public static class EfCoreColumnBuilderExtensions
{
    public static TBuilder HasConversion<TBuilder>(this TBuilder builder, ValueConverter converter)
        where TBuilder : ColumnBuilderBase<TBuilder>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(converter);

        builder.Definition.ValueConverter = converter;
        builder.Definition.ProviderClrType = converter.ProviderClrType;
        return builder;
    }
}
