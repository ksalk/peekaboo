using System.Globalization;
using Microsoft.Data.Sqlite;
using Peekaboo.Api.Data;

namespace Peekaboo.Api.Nvrs;

public sealed class NvrService(AppDatabase database)
{
    private const string Columns =
        "id, name, base_url, transport, sdk_port, created_at, updated_at, time_zone";

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

    public NvrDto Create(
        string name,
        string baseUrl,
        string transport = NvrTransports.Rtsp,
        int sdkPort = NvrTransports.DefaultSdkPort,
        string? timeZone = null)
    {
        ValidateTransport(transport, sdkPort);
        var normalizedTimeZone = NvrValidation.NormalizeTimeZone(timeZone);
        ValidateTimeZone(normalizedTimeZone);

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO nvrs (id, name, base_url, transport, sdk_port, created_at, updated_at, time_zone)
            VALUES ($id, $name, $baseUrl, $transport, $sdkPort, $createdAt, $updatedAt, $timeZone)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$baseUrl", baseUrl);
        command.Parameters.AddWithValue("$transport", transport);
        command.Parameters.AddWithValue("$sdkPort", sdkPort);
        command.Parameters.AddWithValue("$createdAt", Timestamp(now));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(now));
        command.Parameters.AddWithValue("$timeZone", normalizedTimeZone);
        command.ExecuteNonQuery();

        return new NvrDto(id, name, baseUrl, transport, sdkPort, now, now, normalizedTimeZone);
    }

    public NvrDto? Update(
        string id,
        string name,
        string baseUrl,
        string transport,
        int sdkPort,
        string? timeZone = null)
    {
        ValidateTransport(transport, sdkPort);

        var now = DateTimeOffset.UtcNow;

        using var connection = database.Open();
        var normalizedTimeZone = timeZone is null
            ? ReadTimeZone(connection, id) ?? string.Empty
            : NvrValidation.NormalizeTimeZone(timeZone);
        ValidateTimeZone(normalizedTimeZone);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE nvrs
                SET name = $name,
                    base_url = $baseUrl,
                    transport = $transport,
                    sdk_port = $sdkPort,
                    time_zone = $timeZone,
                    updated_at = $updatedAt
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$baseUrl", baseUrl);
            command.Parameters.AddWithValue("$transport", transport);
            command.Parameters.AddWithValue("$sdkPort", sdkPort);
            command.Parameters.AddWithValue("$timeZone", normalizedTimeZone);
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

    private static string? ReadTimeZone(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT time_zone FROM nvrs WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string;
    }

    private static NvrDto ReadNvr(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            ParseTimestamp(reader.GetString(5)),
            ParseTimestamp(reader.GetString(6)),
            reader.GetString(7));

    private static void ValidateTransport(string transport, int sdkPort)
    {
        if (NvrValidation.TransportError(transport) is { } transportError)
        {
            throw new ArgumentException(transportError, nameof(transport));
        }

        if (NvrValidation.SdkPortError(sdkPort) is { } portError)
        {
            throw new ArgumentOutOfRangeException(nameof(sdkPort), portError);
        }
    }

    private static void ValidateTimeZone(string timeZone)
    {
        if (NvrValidation.TimeZoneError(timeZone) is { } error)
        {
            throw new ArgumentException(error, nameof(timeZone));
        }
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static string Timestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
