using System.Text.RegularExpressions;

namespace SharpPersistence.Migrator.Services;

internal static partial class SqlDdlPatterns
{
    internal const string Ident = """(?:"([^"]+)"|([A-Za-z_][A-Za-z0-9_$]*))""";
    internal const string Qualified = $"""(?:{Ident}\s*\.\s*)?{Ident}""";

    [GeneratedRegex("""--[^\r\n]*|/\*[\s\S]*?\*/""", RegexOptions.Compiled)]
    internal static partial Regex Comments();

    [GeneratedRegex("""'(?:''|[^'])*'""", RegexOptions.Compiled)]
    internal static partial Regex SingleQuotedLiteral();

    [GeneratedRegex("""EXECUTE\s+IMMEDIATE\s+'((?:''|[^'])*)'""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex ExecuteImmediate();

    [GeneratedRegex($"""\bCREATE\s+SCHEMA\s+(?:IF\s+NOT\s+EXISTS\s+)?{Ident}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex CreateSchema();

    [GeneratedRegex($"""\bDROP\s+SCHEMA\s+(?:IF\s+EXISTS\s+)?{Ident}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex DropSchema();

    [GeneratedRegex($"""\bCREATE\s+(?:UNLOGGED\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex CreateTable();

    [GeneratedRegex($"""\bALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex AlterTable();

    [GeneratedRegex($"""\bDROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex DropTable();

    [GeneratedRegex($"""\bCREATE\s+(?:OR\s+REPLACE\s+)?SEQUENCE\s+(?:IF\s+NOT\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex CreateSequence();

    [GeneratedRegex($"""\bALTER\s+SEQUENCE\s+(?:IF\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex AlterSequence();

    [GeneratedRegex($"""\bDROP\s+SEQUENCE\s+(?:IF\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex DropSequence();

    [GeneratedRegex($"""\bCREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+NOT\s+EXISTS\s+)?(?:{Qualified}\s+)?ON\s+{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex CreateIndex();

    [GeneratedRegex($"""\bDROP\s+INDEX\s+(?:CONCURRENTLY\s+)?(?:IF\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex DropIndex();

    [GeneratedRegex($"""\b(CREATE|ALTER|DROP)\s+TYPE\s+(?:IF\s+EXISTS\s+)?{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex TypeDdl();

    [GeneratedRegex($"""\bCREATE\s+EXTENSION\s+(?:IF\s+NOT\s+EXISTS\s+)?{Ident}(?:\s+WITH\s+SCHEMA\s+{Ident})?""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex CreateExtension();

    [GeneratedRegex($"""\bDROP\s+EXTENSION\s+(?:IF\s+EXISTS\s+)?{Ident}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex DropExtension();

    [GeneratedRegex($"""(?:\bCONSTRAINT\s+{Ident}\s+)?FOREIGN\s+KEY\s*\([^)]+\)\s*REFERENCES\s+{Qualified}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex ForeignKeyReference();

    [GeneratedRegex($"""\bTABLESPACE\s+{Ident}""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    internal static partial Regex Tablespace();

    internal static string StripComments(string sql) =>
        Comments().Replace(sql, " ");

    internal static string StripStringLiterals(string sql) =>
        SingleQuotedLiteral().Replace(sql, "''");

    internal static IEnumerable<string> EnumerateSanitizedStatements(IEnumerable<string> statements)
    {
        foreach (var raw in statements)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var noComments = StripComments(raw);
            var execMatches = ExecuteImmediate().Matches(noComments);
            if (execMatches.Count > 0)
            {
                foreach (Match m in execMatches)
                {
                    var inner = StripComments(m.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal));
                    var sanitizedInner = StripStringLiterals(inner);
                    if (!string.IsNullOrWhiteSpace(sanitizedInner))
                    {
                        yield return sanitizedInner;
                    }
                }

                var outerWithoutLiterals = StripStringLiterals(noComments);
                if (!string.IsNullOrWhiteSpace(outerWithoutLiterals))
                {
                    yield return outerWithoutLiterals;
                }
            }
            else
            {
                var sanitized = StripStringLiterals(noComments);
                foreach (var part in sanitized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    yield return part;
                }
            }
        }
    }

    internal static (string? Schema, string Name) ParseQualified(Match m, int startGroup, string? defaultSchema)
    {
        var part1 = FirstNonEmpty(m, startGroup, startGroup + 1);
        var part2 = FirstNonEmpty(m, startGroup + 2, startGroup + 3);
        if (!string.IsNullOrWhiteSpace(part1))
        {
            return (part1, part2);
        }

        return (string.IsNullOrWhiteSpace(defaultSchema) ? null : defaultSchema, part2);
    }

    internal static string FirstNonEmpty(Match m, int g1, int g2) =>
        m.Groups[g1].Success ? m.Groups[g1].Value : (m.Groups[g2].Success ? m.Groups[g2].Value : string.Empty);
}
