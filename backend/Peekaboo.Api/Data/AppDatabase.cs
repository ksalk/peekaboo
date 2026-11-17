using Microsoft.Data.Sqlite;

namespace Peekaboo.Api.Data;

public sealed class AppDatabase
{
    private readonly Lock _initLock = new();
    private bool _initialized;

    public AppDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public void Initialize(ILogger<AppDatabase> logger)
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = Open();
            var applied = MigrationRunner.Apply(connection);
            _initialized = true;

            logger.LogInformation(
                "Database ready at {DatabasePath} ({AppliedMigrations} migration(s) applied)",
                Path,
                applied);
        }
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5,
        }.ToString());

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = NORMAL;
            PRAGMA journal_mode = WAL;
            """;
        command.ExecuteNonQuery();

        return connection;
    }
}
