namespace SharpPersistence.Migrator.Constants;

internal static class KnownNames
{
    internal static class AttributeProperties
    {
        internal const string ReadOnly = "ReadOnly";
        internal const string Pluralize = "Pluralize";
        internal const string Exclude = "Exclude";
    }

    internal static class Attributes
    {
        internal const string EntityConfiguration = "EntityConfiguration";
        internal const string BindEntityRegistryDisplay = "[BindEntityRegistry]";
        internal const string EntityRegistryItemDisplay = "[EntityRegistryItem]";
        internal const string AttributeSuffix = "Attribute";
    }

    internal static class Methods
    {
        internal const string Configure = "Configure";
        internal const string OnModelCreating = "OnModelCreating";
        internal const string OnModelCreatingPartial = "OnModelCreatingPartial";
        internal const string ApplySchemaToEfCore = "ApplySchemaToEfCore";
        internal const string Map = "Map";
        internal const string Replace = "Replace";
        internal const string IsRequired = "IsRequired";
        internal const string OwnsOne = "OwnsOne";
    }

    internal static class Types
    {
        internal const string EntityRegistryInterface = "IEntityRegistry";
        internal const string EntityRegistryBuilderInterface = "IEntityRegistryBuilder";
        internal const string EntityConfigurationInterface = "IEntityConfiguration";
        internal const string DbContext = "DbContext";
        internal const string DatabaseModel = "DatabaseModel";
    }

    internal static class Fields
    {
        internal const string SharpPersistenceSchema = "_sharpPersistenceSchema";
    }

    internal static class Namespaces
    {
        internal const string MigratorAbstractions = "SharpPersistence.Migrator.Abstractions";
        internal const string MigratorModels = "SharpPersistence.Migrator.Models";
        internal const string EntityFrameworkCore = "Microsoft.EntityFrameworkCore";
    }
}
