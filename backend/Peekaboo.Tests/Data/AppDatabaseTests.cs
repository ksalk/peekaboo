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

        Assert.Equal(1L, ExecuteScalar(connection, "PRAGMA user_version"));

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
        Assert.Contains("cameras", tables);
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
