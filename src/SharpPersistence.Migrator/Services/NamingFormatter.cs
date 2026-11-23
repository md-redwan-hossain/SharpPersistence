using System.Security.Cryptography;
using System.Text;
using Humanizer;
using SharpPersistence.Enums;

namespace SharpPersistence.Migrator.Services;

public static class NamingFormatter
{
    public static string Format(string identifier, SqlIdentifierCase convention)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return identifier;
        }

        return convention switch
        {
            SqlIdentifierCase.PascalCase => identifier.Pascalize(),
            SqlIdentifierCase.UpperSnakeCase => identifier.Underscore().ToUpperInvariant(),
            _ => identifier.Underscore()
        };
    }

    public static string FormatPrefix(string prefix, SqlIdentifierCase convention)
    {
        return convention == SqlIdentifierCase.LowerSnakeCase
            ? prefix.ToLowerInvariant()
            : prefix.ToUpperInvariant();
    }

    public static int GetMaxIdentifierBytes(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => 63,
        SqlDialect.MySql => 64,
        SqlDialect.SqlServer => 128,
        SqlDialect.Oracle => 128,
        _ => 63
    };

    public static string TruncateIdentifier(string identifier, SqlDialect dialect)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier;
        }

        var maxBytes = GetMaxIdentifierBytes(dialect);
        if (Encoding.UTF8.GetByteCount(identifier) <= maxBytes)
        {
            return identifier;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))[..8];
        var suffix = $"_{hash}";
        var prefixBudget = maxBytes - Encoding.UTF8.GetByteCount(suffix);

        var sb = new StringBuilder();
        var usedBytes = 0;
        foreach (var rune in identifier.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (usedBytes + runeBytes > prefixBudget)
            {
                break;
            }

            sb.Append(rune);
            usedBytes += runeBytes;
        }

        return sb.ToString().TrimEnd('_') + suffix;
    }
}
