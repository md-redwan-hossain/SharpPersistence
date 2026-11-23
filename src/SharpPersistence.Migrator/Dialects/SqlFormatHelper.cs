namespace SharpPersistence.Migrator.Dialects;

internal static class SqlFormatHelper
{
    public static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public static string Qualify(string? schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? Quote(name) : $"{Quote(schema)}.{Quote(name)}";

    public static string EscapeLiteral(string value) =>
        value.Replace("'", "''");
}
