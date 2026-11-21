namespace SharpPersistence.Enums;

[Obsolete("Use SharpPersistence.Enums.SqlIdentifierCase instead.")]
public enum SqlNamingConvention : byte
{
    PascalCase,
    LowerSnakeCase,
    UpperSnakeCase
}