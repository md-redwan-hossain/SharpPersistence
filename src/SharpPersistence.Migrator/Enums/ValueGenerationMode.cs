namespace SharpPersistence.Migrator.Enums;

internal enum ValueGenerationMode : byte
{
    Default,
    Never,
    OnAdd,
    OnAddOrUpdate
}
