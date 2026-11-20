using System.Globalization;
using Microsoft.Data.Sqlite;
using Peekaboo.Api.Data;

namespace Peekaboo.Api.Cameras;

public sealed class CameraService(AppDatabase database)
{
    private const string Columns =
        "c.id, c.nvr_id, n.name, c.name, c.main_stream_id, c.sub_stream_id, c.created_at, c.updated_at";

    public IReadOnlyList<CameraDto> List()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns}
            FROM cameras c
            JOIN nvrs n ON n.id = c.nvr_id
            ORDER BY n.name COLLATE NOCASE, c.name COLLATE NOCASE
            """;

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

    public CameraDto? Create(string nvrId, string name, string mainStreamId, string subStreamId)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        if (!NvrExists(connection, transaction, nvrId))
        {
            transaction.Rollback();
            return null;
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cameras (id, nvr_id, name, main_stream_id, sub_stream_id, created_at, updated_at)
                VALUES ($id, $nvrId, $name, $mainStreamId, $subStreamId, $createdAt, $updatedAt)
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$nvrId", nvrId);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$mainStreamId", mainStreamId);
            command.Parameters.AddWithValue("$subStreamId", subStreamId);
            command.Parameters.AddWithValue("$createdAt", Timestamp(now));
            command.Parameters.AddWithValue("$updatedAt", Timestamp(now));
            command.ExecuteNonQuery();
        }

        var camera = Get(connection, id, transaction);
        transaction.Commit();
        return camera;
    }

    public CameraDto? Update(
        string nvrId,
        string cameraId,
        string name,
        string mainStreamId,
        string subStreamId)
    {
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE cameras
                SET name = $name,
                    main_stream_id = $mainStreamId,
                    sub_stream_id = $subStreamId,
                    updated_at = $updatedAt
                WHERE id = $id AND nvr_id = $nvrId
                """;
            command.Parameters.AddWithValue("$id", cameraId);
            command.Parameters.AddWithValue("$nvrId", nvrId);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$mainStreamId", mainStreamId);
            command.Parameters.AddWithValue("$subStreamId", subStreamId);
            command.Parameters.AddWithValue("$updatedAt", Timestamp(now));

            if (command.ExecuteNonQuery() == 0)
            {
                return null;
            }
        }

        return Get(connection, cameraId);
    }

    public bool Delete(string nvrId, string cameraId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cameras WHERE id = $id AND nvr_id = $nvrId";
        command.Parameters.AddWithValue("$id", cameraId);
        command.Parameters.AddWithValue("$nvrId", nvrId);
        return command.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<string> ListCameraIdsForNvr(string nvrId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM cameras WHERE nvr_id = $nvrId";
        command.Parameters.AddWithValue("$nvrId", nvrId);

        var ids = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public CameraPlaybackSource? GetPlaybackSource(string cameraId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.name, n.id, n.base_url, c.main_stream_id
            FROM cameras c
            JOIN nvrs n ON n.id = c.nvr_id
            WHERE c.id = $id
            """;
        command.Parameters.AddWithValue("$id", cameraId);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new CameraPlaybackSource(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    public CameraStreamTarget? GetStreamTarget(string cameraId, CameraStream stream)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.name, n.base_url, c.main_stream_id, c.sub_stream_id
            FROM cameras c
            JOIN nvrs n ON n.id = c.nvr_id
            WHERE c.id = $id
            """;
        command.Parameters.AddWithValue("$id", cameraId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var streamId = stream == CameraStream.Main
            ? reader.GetString(3)
            : reader.GetString(4);

        return new CameraStreamTarget(
            reader.GetString(0),
            reader.GetString(1),
            stream,
            IsapiStreamUrl.Build(reader.GetString(2), streamId));
    }

    private static CameraDto? Get(
        SqliteConnection connection,
        string id,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {Columns}
            FROM cameras c
            JOIN nvrs n ON n.id = c.nvr_id
            WHERE c.id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCamera(reader) : null;
    }

    private static bool NvrExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string nvrId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM nvrs WHERE id = $id";
        command.Parameters.AddWithValue("$id", nvrId);
        return command.ExecuteScalar() is not null;
    }

    private static CameraDto ReadCamera(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            ParseTimestamp(reader.GetString(6)),
            ParseTimestamp(reader.GetString(7)));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string Timestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
