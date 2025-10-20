using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.ValueObjects;
using SharpPersistence.Parsers.Internals;

namespace SharpPersistence.Parsers;

public class SqlParser
{
    private readonly SqlParserEngine _parser = new();
    private readonly List<string> _validationErrors = [];
    private const string DefaultDirectoryName = "sharp_persistence_sql_files";

    public IParsedSqlStorage ParseFromStorage(string directoryName = DefaultDirectoryName,
        bool throwIfDirectoryNotExists = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);

        var sqlFiles = LoadFromDirectory(directoryName, throwIfDirectoryNotExists);
        foreach (var sqlFile in sqlFiles)
        {
            _parser.Parse(sqlFile.Content, sqlFile.FileName);
        }

        ThrowExceptionIfErrorsExist();
        return _parser.ParsedSqlStatements;
    }

    public IParsedSqlStorage ParseFromString(string sqlContent, string sqlFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlFileName);

        _parser.Parse(sqlContent, sqlFileName);
        ThrowExceptionIfErrorsExist();
        return _parser.ParsedSqlStatements;
    }

    private IEnumerable<SqlFile> LoadFromDirectory(string directoryName, bool throwIfDirectoryNotExists = false)
    {
        ArgumentNullException.ThrowIfNull(directoryName);

        var path = Path.IsPathRooted(directoryName)
            ? directoryName
            : Path.Combine(AppContext.BaseDirectory, directoryName);

        if (Directory.Exists(path))
        {
            return GetSqlFiles(path);
        }

        if (throwIfDirectoryNotExists)
        {
            _validationErrors.Add($"{directoryName}: error: No such directory exists.");
        }

        return [];
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