using System.Globalization;
using Microsoft.Data.Sqlite;
using Peekaboo.Api.Data;

namespace Peekaboo.Api.Nvrs;

public sealed class NvrService(AppDatabase database)
{
    private const string Columns = "id, name, base_url, created_at, updated_at";

    public IReadOnlyList<NvrDto> List()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM nvrs ORDER BY name COLLATE NOCASE";

        var nvrs = new List<NvrDto>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            nvrs.Add(ReadNvr(reader));
        }

        return nvrs;
    }

    public NvrDto? Get(string id)
    {
        using var connection = database.Open();
        return Get(connection, id);
    }

    public NvrDto Create(string name, string baseUrl)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO nvrs (id, name, base_url, created_at, updated_at)
            VALUES ($id, $name, $baseUrl, $createdAt, $updatedAt)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$baseUrl", baseUrl);
        command.Parameters.AddWithValue("$createdAt", Timestamp(now));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(now));
        command.ExecuteNonQuery();

        return new NvrDto(id, name, baseUrl, now, now);
    }

    public NvrDto? Update(string id, string name, string baseUrl)
    {
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE nvrs
                SET name = $name, base_url = $baseUrl, updated_at = $updatedAt
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$baseUrl", baseUrl);
            command.Parameters.AddWithValue("$updatedAt", Timestamp(now));

            if (command.ExecuteNonQuery() == 0)
            {
                return null;
            }
        }

        return Get(connection, id);
    }

    /// <summary>
    /// Deletes an NVR and returns the IDs of its cameras so active streams can be
    /// stopped. Returns null when the NVR does not exist.
    /// </summary>
    public IReadOnlyList<string>? Delete(string id)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var cameraIds = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM cameras WHERE nvr_id = $id";
            command.Parameters.AddWithValue("$id", id);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cameraIds.Add(reader.GetString(0));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM nvrs WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);

            if (command.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                return null;
            }
        }

        transaction.Commit();
        return cameraIds;
    }

    private static NvrDto? Get(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM nvrs WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadNvr(reader) : null;
    }

    private static NvrDto ReadNvr(SqliteDataReader reader) =>
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
