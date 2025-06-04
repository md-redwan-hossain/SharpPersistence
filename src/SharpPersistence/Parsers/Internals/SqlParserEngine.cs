using System.Text;
using System.Text.RegularExpressions;
using SharpPersistence.Abstractions.Primitives;

namespace SharpPersistence.Parsers.Internals;

internal class SqlParserEngine
{
    private static readonly string[] NewLineSeparators = ["\r\n", "\n", "\r"];
    private const string StartPrefix = "start:";
    private const string EndPrefix = "end:";
    private string _sqlFileName = string.Empty;

    internal ParsedSqlStorage ParsedSqlStatements { get; } = new();
    internal List<string> ValidationErrors { get; } = [];

    internal void Parse(string source, string sqlFileName)
    {
        _sqlFileName = sqlFileName;
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source))
        {
            ValidationErrors.Add(FormatParserExceptionMessage("Data source is empty or consists only in whitespace.",
                sqlFileName: _sqlFileName));

            return;
        }

        var lines = source.Split(NewLineSeparators, StringSplitOptions.None);
        var sqlBlocks = new Dictionary<string, SqlBlockInfo>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = new SqlLine { Number = i + 1, Text = lines[i] };

            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            if (IsStartBlock(ref line))
            {
                var tagName = ExtractTagName(ref line, StartPrefix);
                switch (string.IsNullOrEmpty(tagName))
                {
                    case false when sqlBlocks.ContainsKey(tagName):
                        ValidationErrors.Add(FormatParserExceptionMessage(
                            "Duplicate tag '{0}' found. Each tag must be unique.",
                            actualValue: tagName,
                            lineNumber: line.Number,
                            column: GetColumnPosition(line.Text, StartPrefix),
                            sqlFileName: _sqlFileName));
                        break;
                    case false:
                        sqlBlocks[tagName] = new SqlBlockInfo
                        {
                            StartLine = line.Number,
                            StartFound = true
                        };
                        break;
                }
            }
            else if (IsEndBlock(ref line))
            {
                var tagName = ExtractTagName(ref line, EndPrefix);
                switch (string.IsNullOrEmpty(tagName))
                {
                    case false when sqlBlocks.ContainsKey(tagName):
                        sqlBlocks[tagName] = sqlBlocks[tagName] with { EndLine = line.Number, EndFound = true };
                        break;
                    case false:
                        ValidationErrors.Add(FormatParserExceptionMessage(
                            "End tag '{0}' found without corresponding start tag.",
                            actualValue: tagName,
                            lineNumber: line.Number,
                            column: GetColumnPosition(line.Text, EndPrefix),
                            sqlFileName: _sqlFileName));
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
                    sqlFileName: _sqlFileName));
            }

            if (blockInfo.EndFound is false)
            {
                ValidationErrors.Add(FormatParserExceptionMessage(
                    "End tag '{0}' is missing.",
                    actualValue: key,
                    lineNumber: blockInfo.StartLine,
                    sqlFileName: _sqlFileName));
            }
        }

        foreach (var (tagName, blockInfo) in sqlBlocks.Where(b => b.Value is { StartFound: true, EndFound: true }))
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
                    actualValue: tagName,
                    lineNumber: blockInfo.StartLine,
                    sqlFileName: _sqlFileName));
            }
            else
            {
                ParsedSqlStatements.TryAdd(tagName, finalSql);
            }
        }
    }

    private static bool IsStartBlock(ref SqlLine sqlLine) => Regex.IsMatch(sqlLine.Text, @"^\s*--\s*start\s*:");

    private static bool IsEndBlock(ref SqlLine sqlLine) => Regex.IsMatch(sqlLine.Text, @"^\s*--\s*end\s*:");

    private string ExtractTagName(ref SqlLine sqlLine, string prefix)
    {
        var parts = sqlLine.Text.Split(':', 2);
        if (parts.Length < 2)
        {
            ValidationErrors.Add(FormatParserExceptionMessage(
                "Invalid {0} tag format. Expected format: -- {0} tagName",
                actualValue: prefix.TrimEnd(':'),
                lineNumber: sqlLine.Number,
                column: 1,
                sqlFileName: _sqlFileName));
            return string.Empty;
        }

        var extractedTag = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(extractedTag))
        {
            ValidationErrors.Add(FormatParserExceptionMessage(
                "The tag name is empty in {0} declaration.",
                actualValue: prefix.TrimEnd(':'),
                lineNumber: sqlLine.Number,
                column: GetColumnPosition(sqlLine.Text, prefix),
                sqlFileName: _sqlFileName));
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
        int? lineNumber = null, int? column = null, string? sqlFileName = null)
    {
        var formattedMessage = actualValue != null ? string.Format(message, actualValue) : message;

        if (AreNotNull(sqlFileName, lineNumber, column))
        {
            return $"{sqlFileName}:(line {lineNumber}, col {column}): error: {formattedMessage}";
        }

        if (AreNotNull(lineNumber, column))
        {
            return $"Parsing error (line {lineNumber}, col {column}): error: {formattedMessage}";
        }

        if (sqlFileName != null && lineNumber != null)
        {
            return $"{sqlFileName}:(line {lineNumber}): error: {formattedMessage}";
        }

        if (lineNumber != null)
        {
            return $"Parsing error (line {lineNumber}): error: {formattedMessage}";
        }

        if (sqlFileName != null)
        {
            return $"{sqlFileName}: error: {formattedMessage}";
        }

        return $"Parsing error: {formattedMessage}";
    }

    private static bool AreNotNull(params object?[] elements) => elements.All(x => x is not null);
}