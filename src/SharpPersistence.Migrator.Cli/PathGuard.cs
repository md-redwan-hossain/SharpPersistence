namespace SharpPersistence.Migrator.Cli;

internal static class PathGuard
{
    public static string EnsureReadableDirectory(string? path, string optionName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{optionName} cannot be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new IOException($"{optionName} '{fullPath}' points to an existing file, not a directory.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"{optionName} directory not found: '{fullPath}'.");
        }

        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(fullPath).GetEnumerator();
            _ = enumerator.MoveNext();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"{optionName} directory is not readable: '{fullPath}'.", ex);
        }

        return fullPath;
    }

    public static string EnsureReadableFileOrDirectory(string? path, string optionName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{optionName} cannot be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            try
            {
                using var _ = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"{optionName} file is not readable: '{fullPath}'.", ex);
            }

            return fullPath;
        }

        if (Directory.Exists(fullPath))
        {
            return EnsureReadableDirectory(fullPath, optionName);
        }

        throw new FileNotFoundException($"{optionName} path not found: '{fullPath}'.", fullPath);
    }

    public static string EnsureWritableDirectory(string? path, string optionName, bool dryRun = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{optionName} cannot be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            throw new IOException($"{optionName} '{fullPath}' points to an existing file, not a directory.");
        }

        if (dryRun)
        {
            return fullPath;
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            var probePath = Path.Combine(fullPath, $".sp_write_probe_{Guid.NewGuid():N}.tmp");
            using var _ = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException($"{optionName} directory is not writable: '{fullPath}'.", ex);
        }
        catch (IOException ex)
        {
            throw new IOException($"{optionName} directory is not writable: '{fullPath}'. {ex.Message}", ex);
        }

        return fullPath;
    }
}
