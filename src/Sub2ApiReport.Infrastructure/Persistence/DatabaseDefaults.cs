using Microsoft.Data.Sqlite;

namespace Sub2ApiReport.Infrastructure.Persistence;

public static class DatabaseDefaults
{
    public const string ConnectionString =
        "Data Source=data/db/sub2api-report.db;Foreign Keys=True;Default Timeout=5;Pooling=True";

    public static string ResolveConnectionString(
        string connectionString,
        string contentRootPath,
        string? applicationBasePath = null)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || builder.DataSource == ":memory:"
            || builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(builder.DataSource))
        {
            return connectionString;
        }

        builder.DataSource = ResolvePath(builder.DataSource, contentRootPath, applicationBasePath);
        return builder.ConnectionString;
    }

    public static string ResolvePath(
        string path,
        string contentRootPath,
        string? applicationBasePath = null)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var executableBasePath = Path.GetFullPath(applicationBasePath ?? AppContext.BaseDirectory);
        var basePath = FindRepositoryRoot(contentRootPath)
            ?? FindRepositoryRoot(executableBasePath)
            ?? executableBasePath;
        var resolvedPath = Path.GetFullPath(path, basePath);
        var relativePath = Path.GetRelativePath(basePath, resolvedPath);
        if (relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                "A relative application data path cannot resolve outside the repository or application directory.");
        }

        return resolvedPath;
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sub2ApiReport.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
