using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.ValueObjects;
using SharpPersistence.Parsers.Internals;

namespace SharpPersistence.Parsers;

public class SqlParser
{
    private readonly SqlParserEngine _parser = new();
    private readonly List<string> _validationErrors = [];
    private const string DefaultDirectoryName = "sharp_persistence_sql_files";

    public IParsedSqlStorage ParseFromStorage(string directoryName = DefaultDirectoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);

        var sqlFiles = LoadFromDirectory(directoryName);
        foreach (var sqlFile in sqlFiles)
        {
            _parser.Parse(sqlFile.Content, sqlFile.FileName);
        }

        ThrowExceptionIfErrorsExist();
        return _parser.ParsedSqlStatements;
    }

    private IEnumerable<SqlFile> LoadFromDirectory(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);

        var path = Path.IsPathRooted(directoryName)
            ? directoryName
            : Path.Combine(AppContext.BaseDirectory, directoryName);

        if (Directory.Exists(path) is false)
        {
            _validationErrors.Add($"{directoryName}: error: No such directory exists.");
            return [];
        }

        return GetSqlFiles(path);
    }

    private static IEnumerable<SqlFile> GetSqlFiles(string directoryName)
    {
        var files = Directory.GetFiles(directoryName, "*.sql", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            yield return new SqlFile
            {
                FileName = Path.GetFileName(file),
                Content = File.ReadAllText(file)
            };
        }
    }

    private void ThrowExceptionIfErrorsExist()
    {
        List<string> allErrors = [];

        if (_validationErrors.Count > 0)
        {
            allErrors.AddRange(_validationErrors);
        }

        if (_parser.ValidationErrors.Count > 0)
        {
            allErrors.AddRange(_parser.ValidationErrors);
        }

        if (allErrors.Count > 0)
        {
            var combinedMessage = string.Join(Environment.NewLine, allErrors);
            throw new InvalidOperationException(combinedMessage);
        }
    }
}