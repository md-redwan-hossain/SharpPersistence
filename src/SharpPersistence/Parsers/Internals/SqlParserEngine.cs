using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using SharpPersistence.Abstractions.ValueObjects;

namespace SharpPersistence.Parsers.Internals;

internal class SqlParserEngine
{
    private static readonly string[] NewLineSeparators = ["\r\n", "\n", "\r"];
    private const string StartPrefix = "#start#";
    private const string EndPrefix = "#end#";
    private string _sqlSource = string.Empty;

    internal ParsedSqlStorage ParsedSqlStatements { get; } = new();
    internal List<string> ValidationErrors { get; } = [];

    internal void Parse(string source, string sqlSource)
    {
        _sqlSource = sqlSource;
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var lines = source.Split(NewLineSeparators, StringSplitOptions.None);
        var sqlBlocks = new ConcurrentDictionary<string, SqlBlockInfo>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = new SqlLine { Number = i + 1, Text = lines[i] };

            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            if (IsStartBlock(ref line))
            {
                var uniqueTag = ExtractUniqueName(ref line, StartPrefix);
                switch (string.IsNullOrEmpty(uniqueTag))
                {
                    case false when sqlBlocks.ContainsKey(uniqueTag):
                        ValidationErrors.Add(FormatParserExceptionMessage(
                            "Duplicate tag '{0}' found. Each tag must be unique.",
                            actualValue: uniqueTag,
                            lineNumber: line.Number,
                            column: GetColumnPosition(line.Text, StartPrefix),
                            sqlSource: _sqlSource));
                        break;
                    case false:
                        sqlBlocks[uniqueTag] = new SqlBlockInfo
                        {
                            StartLine = line.Number,
                            StartFound = true
                        };
                        break;
                }
            }
            else if (IsEndBlock(ref line))
            {
                var uniqueTag = ExtractUniqueName(ref line, EndPrefix);
                switch (string.IsNullOrEmpty(uniqueTag))
                {
                    case false when sqlBlocks.ContainsKey(uniqueTag):
                        sqlBlocks[uniqueTag] = sqlBlocks[uniqueTag] with { EndLine = line.Number, EndFound = true };
                        break;
                    case false:
                        ValidationErrors.Add(FormatParserExceptionMessage(
                            "End tag '{0}' found without corresponding start tag.",
                            actualValue: uniqueTag,
                            lineNumber: line.Number,
                            column: GetColumnPosition(line.Text, EndPrefix),
                            sqlSource: _sqlSource));
                        break;
                }
            }
        }

        foreach (var (key, blockInfo) in sqlBlocks)
        {
            if (!blockInfo.StartFound)
            {
                ValidationErrors.Add(FormatParserExceptionMessage(
                    "Start tag '{0}' is missing.",
                    actualValue: key,
                    sqlSource: _sqlSource));
            }

            if (!blockInfo.EndFound)
            {
                ValidationErrors.Add(FormatParserExceptionMessage(
                    "End tag '{0}' is missing.",
                    actualValue: key,
                    lineNumber: blockInfo.StartLine,
                    sqlSource: _sqlSource));
            }
        }

        foreach (var (uniqueTag, blockInfo) in sqlBlocks.Where(b => b.Value is { StartFound: true, EndFound: true }))
        {
            var sqlContent = new StringBuilder();

            for (var i = blockInfo.StartLine; i < blockInfo.EndLine - 1; i++)
            {
                var lineText = lines[i].Trim();
                if (!string.IsNullOrEmpty(lineText))
                {
                    sqlContent.AppendLine(lineText);
                }
            }

            var finalSql = sqlContent.ToString().Trim();
            if (string.IsNullOrEmpty(finalSql))
            {
                ValidationErrors.Add(FormatParserExceptionMessage(
                    "SQL block '{0}' is empty.",
                    actualValue: uniqueTag,
                    lineNumber: blockInfo.StartLine,
                    sqlSource: _sqlSource));
            }
            else
            {
                ParsedSqlStatements.TryAdd(uniqueTag, finalSql);
            }
        }
    }

    private static bool IsStartBlock(ref SqlLine sqlLine) => Regex.IsMatch(sqlLine.Text, @"^\s*--\s*#start#");

    private static bool IsEndBlock(ref SqlLine sqlLine) => Regex.IsMatch(sqlLine.Text, @"^\s*--\s*#end#");

    private string ExtractUniqueName(ref SqlLine sqlLine, string prefix)
    {
        // find the prefix and extract everything after it
        var trimmedLine = sqlLine.Text.Trim();
        var prefixIndex = trimmedLine.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);

        if (prefixIndex == -1)
        {
            ValidationErrors.Add(FormatParserExceptionMessage(
                "Invalid {0} tag format. Expected format: -- {0} uniqueTag",
                actualValue: prefix,
                lineNumber: sqlLine.Number,
                column: 1,
                sqlSource: _sqlSource));
            return string.Empty;
        }

        // Extract everything after the prefix
        var startIndex = prefixIndex + prefix.Length;
        var extractedTag = startIndex < trimmedLine.Length
            ? trimmedLine[startIndex..].Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(extractedTag))
        {
            ValidationErrors.Add(FormatParserExceptionMessage(
                "The tag name is empty in {0} declaration.",
                actualValue: prefix,
                lineNumber: sqlLine.Number,
                column: GetColumnPosition(sqlLine.Text, prefix),
                sqlSource: _sqlSource));

            return string.Empty;
        }

        return extractedTag;
    }

    private static int GetColumnPosition(string lineText, string prefix)
    {
        var index = lineText.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? index + prefix.Length + 1 : 1;
    }

    private static string FormatParserExceptionMessage(string message, object? actualValue = null,
        int? lineNumber = null, int? column = null, string? sqlSource = null)
    {
        var formattedMessage = actualValue is not null ? string.Format(message, actualValue) : message;

        if (AreNotNull(sqlSource, lineNumber, column))
        {
            return $"{sqlSource}:(line {lineNumber}, col {column}): error: {formattedMessage}";
        }

        if (AreNotNull(lineNumber, column))
        {
            return $"Parsing error (line {lineNumber}, col {column}): error: {formattedMessage}";
        }

        if (sqlSource is not null && lineNumber is not null)
        {
            return $"{sqlSource}:(line {lineNumber}): error: {formattedMessage}";
        }

        if (lineNumber is not null)
        {
            return $"Parsing error (line {lineNumber}): error: {formattedMessage}";
        }

        if (sqlSource is not null)
        {
            return $"{sqlSource}: error: {formattedMessage}";
        }

        return $"Parsing error: {formattedMessage}";
    }

    private static bool AreNotNull(params object?[] elements) => elements.All(x => x is not null);
}