namespace SharpPersistence.Migrator.Services;

internal static class MigrationScriptHeader
{
    public const string HeaderPrefix = "-- migration-script-id:";

    public static bool TryParseId(string sql, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        using var reader = new StringReader(sql);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var raw = trimmed[HeaderPrefix.Length..].Trim();
                return Guid.TryParse(raw, out id) && id != Guid.Empty;
            }

            if (!trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }
        }

        return false;
    }

    public static (string StampedSql, Guid Id, bool AlreadyHadHeader) StampHeader(string sql, Guid? id = null)
    {
        if (TryParseId(sql, out var existingId))
        {
            return (sql, existingId, true);
        }

        var assignedId = id is { } g && g != Guid.Empty ? g : Guid.CreateVersion7();
        var newline = sql.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var stamped = $"{HeaderPrefix} {assignedId:D}{newline}{sql}";
        return (stamped, assignedId, false);
    }

    public static string StripHeader(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return sql;
        }

        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var headerLineIndex = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                headerLineIndex = i;
                break;
            }

            if (!trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (headerLineIndex < 0)
        {
            return normalized;
        }

        return string.Join("\n", lines.Where((_, idx) => idx != headerLineIndex));
    }
}
