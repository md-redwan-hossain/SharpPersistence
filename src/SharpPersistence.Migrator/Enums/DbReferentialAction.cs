namespace SharpPersistence.Migrator.Enums;

public enum DbReferentialAction : byte
{
    NoAction,
    Restrict,
    Cascade,
    SetNull,
    SetDefault
}
