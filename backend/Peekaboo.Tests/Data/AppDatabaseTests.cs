using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Data;
using Xunit;

namespace Peekaboo.Tests.Data;

public sealed class AppDatabaseTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-database-tests-").FullName;

    [Fact]
    public void Initialize_creates_schema_and_is_idempotent()
    {
        var database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));

        database.Initialize(NullLogger<AppDatabase>.Instance);
        database.Initialize(NullLogger<AppDatabase>.Instance);

        using var connection = database.Open();

        Assert.Equal(4L, ExecuteScalar(connection, "PRAGMA user_version"));

        var tables = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Contains("users", tables);
        Assert.Contains("sessions", tables);
        Assert.Contains("nvrs", tables);
        Assert.Contains("cameras", tables);

        var cameraColumns = Columns(connection, "cameras");
        Assert.Contains("nvr_id", cameraColumns);
        Assert.Contains("main_stream_id", cameraColumns);
        Assert.Contains("sub_stream_id", cameraColumns);
        Assert.DoesNotContain("url", cameraColumns);

        var nvrColumns = Columns(connection, "nvrs");
        Assert.Contains("transport", nvrColumns);
        Assert.Contains("sdk_port", nvrColumns);
        Assert.Contains("time_zone", nvrColumns);
    }

    [Fact]
    public void Migration_replaces_url_based_cameras()
    {
        var path = Path.Combine(_directory, "legacy.db");

        var initialMigration = MigrationRunner.LoadMigrations()
            .First(migration => migration.Version == 1);

        using (var legacy = new SqliteConnection($"Data Source={path}"))
        {
            legacy.Open();
            using var command = legacy.CreateCommand();
            command.CommandText = initialMigration.Sql + """

                INSERT INTO cameras (id, name, url, created_at, updated_at)
                VALUES ('old', 'Legacy', 'rtsp://legacy.local/stream', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        var database = new AppDatabase(path);
        database.Initialize(NullLogger<AppDatabase>.Instance);

        using var connection = database.Open();
        Assert.Equal(4L, ExecuteScalar(connection, "PRAGMA user_version"));
        Assert.Equal(0L, ExecuteScalar(connection, "SELECT COUNT(*) FROM cameras"));
        Assert.Contains("nvr_id", Columns(connection, "cameras"));
        Assert.Contains("transport", Columns(connection, "nvrs"));
    }

    [Fact]
    public void Migration_backfills_transport_defaults_for_existing_nvrs()
    {
        var path = Path.Combine(_directory, "backfill.db");
        var initialMigrations = MigrationRunner.LoadMigrations()
            .Where(migration => migration.Version <= 2)
            .Select(migration => migration.Sql);

        using (var legacy = new SqliteConnection($"Data Source={path}"))
        {
            legacy.Open();
            using var command = legacy.CreateCommand();
            command.CommandText = string.Join("\n", initialMigrations) + """

                INSERT INTO nvrs (id, name, base_url, created_at, updated_at)
                VALUES ('old', 'Legacy NVR', 'rtsp://legacy.local:554',
                        '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }

        var database = new AppDatabase(path);
        database.Initialize(NullLogger<AppDatabase>.Instance);

        using var connection = database.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT transport, sdk_port FROM nvrs WHERE id = 'old'";
        using var reader = read.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("rtsp", reader.GetString(0));
        Assert.Equal(8000, reader.GetInt32(1));
    }

    [Fact]
    public void Migration_backfills_time_zone_for_existing_nvrs()
    {
        var path = Path.Combine(_directory, "time-zone-backfill.db");
        var migrations = MigrationRunner.LoadMigrations()
            .Where(migration => migration.Version <= 3)
            .Select(migration => migration.Sql);

        using (var legacy = new SqliteConnection($"Data Source={path}"))
        {
            legacy.Open();
            using var command = legacy.CreateCommand();
            command.CommandText = string.Join("\n", migrations) + """

                INSERT INTO nvrs (id, name, base_url, created_at, updated_at)
                VALUES ('old', 'Legacy NVR', 'rtsp://legacy.local:554',
                        '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }

        var database = new AppDatabase(path);
        database.Initialize(NullLogger<AppDatabase>.Instance);

        using var connection = database.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT time_zone FROM nvrs WHERE id = 'old'";
        Assert.Equal(string.Empty, read.ExecuteScalar());
    }

    [Fact]
    public void Initialize_creates_missing_database_directory()
    {
        var nestedPath = Path.Combine(_directory, "nested", "deeper", "peekaboo.db");
        var database = new AppDatabase(nestedPath);

        database.Initialize(NullLogger<AppDatabase>.Instance);

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void Initialize_refuses_database_with_newer_schema()
    {
        var path = Path.Combine(_directory, "newer.db");
        new AppDatabase(path).Initialize(NullLogger<AppDatabase>.Instance);

        var seeded = new AppDatabase(path);
        using (var connection = seeded.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 999";
            command.ExecuteNonQuery();
        }

        var reopened = new AppDatabase(path);
        var exception = Assert.Throws<InvalidOperationException>(
            () => reopened.Initialize(NullLogger<AppDatabase>.Instance));

        Assert.Contains("newer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> Columns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";

        var columns = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
