// Adapted from Humanizer's Vocabularies.Default English pluralization rules (MIT License).
using System.Text.RegularExpressions;

namespace SharpPersistence.Migrator.Generators;

internal static class EnglishPluralizer
{
    private readonly struct PluralRule(string pattern, string replacement)
    {
        private readonly Regex _regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly string _replacement = replacement;

        public string? Apply(string word) =>
            _regex.IsMatch(word) ? _regex.Replace(word, _replacement) : null;
    }

    private static readonly HashSet<string> Uncountables = new(StringComparer.OrdinalIgnoreCase)
    {
        "equipment", "information", "rice", "money", "species", "series", "fish", "sheep",
        "deer", "aircraft", "oz", "tsp", "tbsp", "ml", "l", "water", "waters", "semen",
        "sperm", "bison", "grass", "hair", "mud", "elk", "luggage", "moose", "offspring",
        "salmon", "shrimp", "someone", "swine", "trout", "tuna", "corps", "scissors",
        "means", "mail", "data", "metadata", "info", "news", "staff", "feedback", "software",
        "hardware", "access", "settings"
    };

    private static readonly Dictionary<string, string> Irregulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = "people",
        ["man"] = "men",
        ["woman"] = "women",
        ["child"] = "children",
        ["ox"] = "oxen",
        ["mouse"] = "mice",
        ["louse"] = "lice",
        ["goose"] = "geese",
        ["foot"] = "feet",
        ["tooth"] = "teeth",
        ["criterion"] = "criteria",
        ["phenomenon"] = "phenomena",
        ["datum"] = "data",
        ["medium"] = "media",
        ["curriculum"] = "curricula",
        ["memorandum"] = "memoranda",
        ["index"] = "indices",
        ["matrix"] = "matrices",
        ["vertex"] = "vertices",
        ["appendix"] = "appendices",
        ["quiz"] = "quizzes",
        ["campus"] = "campuses",
        ["status"] = "statuses",
        ["alias"] = "aliases",
        ["bus"] = "buses",
        ["move"] = "moves",
        ["leaf"] = "leaves",
        ["wave"] = "waves",
        ["curve"] = "curves",
        ["passerby"] = "passersby"
    };

    private static readonly HashSet<string> IrregularPlurals = new(Irregulars.Values, StringComparer.OrdinalIgnoreCase);

    // Ordered from most-specific to general (applied in reverse, matching Humanizer's Vocabularies.Default)
    private static readonly PluralRule[] Rules =
    [
        new("$", "s"),
        new("s$", "s"),
        new("(ax|test)is$", "$1es"),
        new("(octop|vir|alumn|fung|cact|foc|hippopotam|radi|stimul|syllab|nucle)us$", "$1i"),
        new("(alias|bias|iris|status|campus|apparatus|virus|walrus|trellis)$", "$1es"),
        new("(buffal|tomat|volcan|ech|embarg|her|mosquit|potat|torped|vet)o$", "$1oes"),
        new("([dti])um$", "$1a"),
        new("sis$", "ses"),
        new("(?:([^f])fe|([lr])f)$", "$1$2ves"),
        new("(hive)$", "$1s"),
        new("([^aeiouy]|qu)y$", "$1ies"),
        new("(x|ch|ss|sh)$", "$1es"),
        new("(matr|vert|ind|d)ix|ex$", "$1ices"),
        new("([m|l])ouse$", "$1ice"),
        new("^(ox)$", "$1en"),
        new("(quiz)$", "$1zes"),
        new("(buz|blit|walt)z$", "$1zes"),
        new("(hoo|lea|loa|thie)f$", "$1ves"),
        new("(alumn|alg|larv|vertebr)a$", "$1ae"),
        new("(criteri|phenomen)on$", "$1a")
    ];

    public static string PluralizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return identifier;
        }

        // Split off the last PascalCase word so compound names like InventoryCategory -> InventoryCategories
        var lastWordStart = FindLastPascalWordStart(identifier);
        var prefix = lastWordStart > 0 ? identifier.Substring(0, lastWordStart) : string.Empty;
        var lastWord = lastWordStart > 0 ? identifier.Substring(lastWordStart) : identifier;

        var pluralizedLast = PluralizeWord(lastWord);
        return prefix + pluralizedLast;
    }

    private static string PluralizeWord(string word)
    {
        if (string.IsNullOrEmpty(word) || Uncountables.Contains(word))
        {
            return word;
        }

        if (Irregulars.TryGetValue(word, out var irregular))
        {
            return MatchCasing(word, irregular);
        }

        // Already plural check for common irregular targets (O(1) lookup)
        if (IrregularPlurals.Contains(word))
        {
            return word;
        }

        for (var i = Rules.Length - 1; i >= 0; i--)
        {
            var result = Rules[i].Apply(word);
            if (result is not null)
            {
                return result;
            }
        }

        return word;
    }

    private static int FindLastPascalWordStart(string identifier)
    {
        for (var i = identifier.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(identifier[i]) &&
                (char.IsLower(identifier[i - 1]) ||
                 char.IsDigit(identifier[i - 1]) ||
                 (i + 1 < identifier.Length && char.IsLower(identifier[i + 1]))))
            {
                return i;
            }
        }

        return 0;
    }

    private static string MatchCasing(string template, string replacement)
    {
        if (template.Length == 0 || replacement.Length == 0)
        {
            return replacement;
        }

        if (char.IsUpper(template[0]))
        {
            return char.ToUpperInvariant(replacement[0]) + replacement.Substring(1);
        }

        return replacement;
    }
}
