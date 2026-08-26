using Microsoft.Data.Sqlite;
using Sub2ApiReport.Infrastructure.Persistence;

namespace Sub2ApiReport.IntegrationTests.Persistence;

public sealed class DatabaseDefaultsTests
{
    [Fact]
    public void ResolveConnectionStringUsesRepositoryRootFromNestedBuildDirectory()
    {
        var repositoryRoot = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Sub2ApiReport.slnx"), string.Empty);
            var contentRoot = Path.Combine(
                repositoryRoot,
                "src",
                "Sub2ApiReport.Migrator",
                "bin",
                "Debug",
                "net10.0");
            Directory.CreateDirectory(contentRoot);

            var resolved = DatabaseDefaults.ResolveConnectionString(
                DatabaseDefaults.ConnectionString,
                contentRoot);
            var dataSource = new SqliteConnectionStringBuilder(resolved).DataSource;

            Assert.Equal(
                Path.Combine(repositoryRoot, "data", "db", "sub2api-report.db"),
                dataSource);
            Assert.Equal(
                Path.Combine(repositoryRoot, "data", "keys"),
                DatabaseDefaults.ResolvePath(Path.Combine("data", "keys"), contentRoot));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveConnectionStringUsesApplicationBaseWhenContentRootIsOutsideRepository()
    {
        var workspaceRoot = CreateTemporaryDirectory();
        try
        {
            var repositoryRoot = Path.Combine(workspaceRoot, "Github", "sub2api-report");
            var applicationBase = Path.Combine(
                repositoryRoot,
                "src",
                "Sub2ApiReport.Api",
                "bin",
                "Debug",
                "net10.0");
            Directory.CreateDirectory(applicationBase);
            File.WriteAllText(Path.Combine(repositoryRoot, "Sub2ApiReport.slnx"), string.Empty);

            var resolved = DatabaseDefaults.ResolveConnectionString(
                DatabaseDefaults.ConnectionString,
                workspaceRoot,
                applicationBase);
            var dataSource = new SqliteConnectionStringBuilder(resolved).DataSource;

            Assert.Equal(
                Path.Combine(repositoryRoot, "data", "db", "sub2api-report.db"),
                dataSource);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveConnectionStringUsesApplicationBaseWhenRepositoryMarkerIsAbsent()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var contentRoot = Path.Combine(temporaryRoot, "ide-working-directory");
            var applicationBase = Path.Combine(temporaryRoot, "application");
            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(applicationBase);

            var resolved = DatabaseDefaults.ResolveConnectionString(
                DatabaseDefaults.ConnectionString,
                contentRoot,
                applicationBase);
            var dataSource = new SqliteConnectionStringBuilder(resolved).DataSource;

            Assert.Equal(
                Path.Combine(applicationBase, "data", "db", "sub2api-report.db"),
                dataSource);
            Assert.Equal(
                Path.Combine(applicationBase, "data", "keys"),
                DatabaseDefaults.ResolvePath(
                    Path.Combine("data", "keys"),
                    contentRoot,
                    applicationBase));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveConnectionStringRejectsRelativePathOutsideRepository()
    {
        var repositoryRoot = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Sub2ApiReport.slnx"), string.Empty);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine("..", "outside", "database.db"),
            }.ConnectionString;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                DatabaseDefaults.ResolveConnectionString(connectionString, repositoryRoot));

            Assert.Contains("cannot resolve outside", exception.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() =>
                DatabaseDefaults.ResolvePath(Path.Combine("..", "outside", "keys"), repositoryRoot));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveConnectionStringPreservesAbsoluteDataSource()
    {
        var contentRoot = CreateTemporaryDirectory();
        try
        {
            var absoluteDataSource = Path.Combine(contentRoot, "database.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = absoluteDataSource,
                ForeignKeys = true,
            }.ConnectionString;

            var resolved = DatabaseDefaults.ResolveConnectionString(connectionString, "unused");

            Assert.Equal(connectionString, resolved);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sub2api-report-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
