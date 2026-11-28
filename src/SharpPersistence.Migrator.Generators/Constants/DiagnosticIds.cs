namespace SharpPersistence.Migrator.Constants;

internal static class DiagnosticIds
{
    internal const string RegistryMustBeSealedPartialImplementingInterface = "SPM001";
    internal const string ConfigurationInvalid = "SPM002";
    internal const string DuplicateEntityConfiguration = "SPM003";
    internal const string RegistryMustDeclareConfigure = "SPM004";
    internal const string DuplicateEntityAcrossRegistries = "SPM005";
    internal const string EntityOrMarkerMustBePublic = "SPM006";
    internal const string OrphanEntityConfiguration = "SPM007";
    internal const string DbContextMustBeSealedPartial = "SPM008";
    internal const string InvalidRegistryItemCombination = "SPM009";
    internal const string DuplicateDbContextBindingAttribute = "SPM010";
    internal const string OrphanEntityRegistryItem = "SPM011";
    internal const string DbContextPropertyNameCollision = "SPM012";
    internal const string DbContextMissingSchemaWiring = "SPM013";
    internal const string RequiredColumnInsideOptionalOwnedNavigation = "SPM014";
    internal const string InvalidEntityReplacement = "SPM015";
    internal const string InvalidRegistryEntityReference = "SPM016";
}
