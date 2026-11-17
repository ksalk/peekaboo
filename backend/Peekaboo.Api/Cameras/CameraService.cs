using System.Globalization;
using Microsoft.Data.Sqlite;
using Peekaboo.Api.Data;

namespace Peekaboo.Api.Cameras;

public sealed class CameraService(AppDatabase database)
{
    private const string Columns = "id, name, url, created_at, updated_at";

    public IReadOnlyList<CameraDto> List()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM cameras ORDER BY name COLLATE NOCASE";

        var cameras = new List<CameraDto>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cameras.Add(ReadCamera(reader));
        }

        return cameras;
    }

    public CameraDto? Get(string id)
    {
        using var connection = database.Open();
        return Get(connection, id);
    }

    public CameraDto Create(string name, string url)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cameras (id, name, url, created_at, updated_at)
            VALUES ($id, $name, $url, $createdAt, $updatedAt)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$createdAt", Timestamp(now));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(now));
        command.ExecuteNonQuery();

        return new CameraDto(id, name, url, now, now);
    }

    public CameraDto? Update(string id, string name, string url)
    {
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE cameras
                SET name = $name, url = $url, updated_at = $updatedAt
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$url", url);
            command.Parameters.AddWithValue("$updatedAt", Timestamp(now));

            if (command.ExecuteNonQuery() == 0)
            {
                return null;
            }
        }

        return Get(connection, id);
    }

    public bool Delete(string id)
    {
        // TODO(streaming): stop an active stream for this camera so deletion cannot
        // leave an ffmpeg process consuming bandwidth.
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cameras WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    private static CameraDto? Get(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM cameras WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCamera(reader) : null;
    }

    private static CameraDto ReadCamera(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            ParseTimestamp(reader.GetString(3)),
            ParseTimestamp(reader.GetString(4)));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string Timestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
