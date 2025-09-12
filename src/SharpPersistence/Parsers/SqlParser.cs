using System.Reflection;
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

    public IParsedSqlStorage ParseFromCallingAssembly()
    {
        var callingAssembly = Assembly.GetCallingAssembly();
        var sqlFiles = LoadFromAssemblyDirectory(callingAssembly);

        foreach (var sqlFile in sqlFiles)
        {
            _parser.Parse(sqlFile.Content, sqlFile.FileName);
        }

        ThrowExceptionIfErrorsExist();
        return _parser.ParsedSqlStatements;
    }

    private IEnumerable<SqlFile> LoadFromAssemblyDirectory(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var assemblyLocation = assembly.Location;
        if (string.IsNullOrEmpty(assemblyLocation))
        {
            _validationErrors.Add($"Could not determine location of assembly: {assembly.FullName}");
            return [];
        }

        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);

        if (Directory.Exists(assemblyDirectory) is false)
        {
            _validationErrors.Add($"{assemblyDirectory}: error: Assembly directory does not exist.");
            return [];
        }

        return GetAllSqlFiles(assemblyDirectory);
    }

    private static IEnumerable<SqlFile> GetAllSqlFiles(string rootDirectory)
    {
        try
        {
            var files = Directory.GetFiles(rootDirectory, "*.sql", SearchOption.AllDirectories);

            return files
                .Select(TryReadSqlFile)
                .Where(sqlFile => sqlFile != null)
                .Cast<SqlFile>();
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we don't have access to
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            // Directory was deleted between check and enumeration
            return [];
        }
    }

    private static SqlFile? TryReadSqlFile(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return new SqlFile
            {
                FileName = Path.GetFileName(filePath),
                Content = content
            };
        }
        catch (IOException)
        {
            // Skip files that can't be read
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Skip files we don't have permission to read
            return null;
        }
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