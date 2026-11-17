using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Peekaboo.Api.Data;
using Peekaboo.Api.Security;

namespace Peekaboo.Api.Auth;

public sealed class AuthService(
    AppDatabase database,
    PasswordHasher passwordHasher,
    ILogger<AuthService> logger)
{
    private const int SessionLifetimeDays = 7;

    private static readonly string DummyPasswordHash =
        new PasswordHasher().Hash("peekaboo-timing-equalizer");

    private const string UserColumns = "id, username, password_hash, is_admin, created_at";

    public void SeedAdmin(string? username, string? password)
    {
        using var connection = database.Open();

        using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM users";
            var userCount = Convert.ToInt64(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (userCount > 0)
            {
                logger.LogInformation("Existing user accounts found; skipping initial admin creation.");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "No user accounts exist yet. Set ADMIN_USERNAME and ADMIN_PASSWORD " +
                "to create the initial administrator account.");
        }

        var usernameError = AuthValidation.UsernameError(username);
        if (usernameError is not null)
        {
            throw new InvalidOperationException($"ADMIN_USERNAME is invalid: {usernameError}");
        }

        var passwordError = AuthValidation.PasswordError(password);
        if (passwordError is not null)
        {
            throw new InvalidOperationException($"ADMIN_PASSWORD is invalid: {passwordError}");
        }

        var admin = TryCreateUser(username, password, isAdmin: true)
            ?? throw new InvalidOperationException("Failed to create the initial administrator account.");

        logger.LogInformation("Created initial administrator account {Username}.", admin.Username);
    }

    public UserRecord? Authenticate(string username, string password)
    {
        var normalizedUsername = username.Trim();

        using var connection = database.Open();
        var user = FindUser(
            connection,
            $"SELECT {UserColumns} FROM users WHERE username = $value",
            transaction: null,
            normalizedUsername);

        // Always run the KDF so a missing account and a wrong password take comparable time.
        var verified = passwordHasher.Verify(password, user?.PasswordHash ?? DummyPasswordHash);

        if (user is null || !verified)
        {
            logger.LogInformation("Authentication failed for username {Username}", normalizedUsername);
            return null;
        }

        return user;
    }

    public UserDto? GetUser(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, username, is_admin, created_at FROM users WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadUserDto(reader) : null;
    }

    public IReadOnlyList<UserDto> ListUsers()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, username, is_admin, created_at FROM users ORDER BY username COLLATE NOCASE";

        var users = new List<UserDto>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            users.Add(ReadUserDto(reader));
        }

        return users;
    }

    public UserDto? TryCreateUser(string username, string password, bool isAdmin)
    {
        var normalizedUsername = username.Trim();
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var passwordHash = passwordHasher.Hash(password);

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, username, password_hash, is_admin, created_at)
            VALUES ($id, $username, $passwordHash, $isAdmin, $createdAt)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$username", normalizedUsername);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$isAdmin", isAdmin ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", Timestamp(createdAt));

        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            // SQLITE_CONSTRAINT_UNIQUE: the username collation is case-insensitive.
            return null;
        }

        return new UserDto(id, normalizedUsername, isAdmin, createdAt);
    }

    public DeleteUserResult DeleteUser(string userId, string currentUserId)
    {
        if (string.Equals(userId, currentUserId, StringComparison.Ordinal))
        {
            return DeleteUserResult.CannotDeleteSelf;
        }

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        var target = FindUser(
            connection,
            $"SELECT {UserColumns} FROM users WHERE id = $value",
            transaction,
            userId);

        if (target is null)
        {
            return DeleteUserResult.NotFound;
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM users WHERE id = $id";
            deleteCommand.Parameters.AddWithValue("$id", userId);
            deleteCommand.ExecuteNonQuery();
        }

        // The delete above takes SQLite's write lock, so the post-delete count is
        // authoritative even when two administrators are removed concurrently.
        if (target.IsAdmin)
        {
            using var countCommand = connection.CreateCommand();
            countCommand.Transaction = transaction;
            countCommand.CommandText = "SELECT COUNT(*) FROM users WHERE is_admin = 1";
            var adminCount = Convert.ToInt64(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (adminCount == 0)
            {
                return DeleteUserResult.LastAdmin;
            }
        }

        transaction.Commit();
        return DeleteUserResult.Deleted;
    }

    public ChangePasswordResult ChangePassword(
        string userId,
        string currentPassword,
        string newPassword,
        string? keepSessionTokenHash)
    {
        using var connection = database.Open();
        var user = FindUser(
            connection,
            $"SELECT {UserColumns} FROM users WHERE id = $value",
            transaction: null,
            userId);

        if (user is null)
        {
            return ChangePasswordResult.NotFound;
        }

        if (!passwordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return ChangePasswordResult.WrongPassword;
        }

        return SetPassword(userId, newPassword, keepSessionTokenHash)
            ? ChangePasswordResult.Changed
            : ChangePasswordResult.NotFound;
    }

    public bool ResetPassword(string userId, string newPassword, string? keepSessionTokenHash) =>
        SetPassword(userId, newPassword, keepSessionTokenHash);

    public (string Token, DateTimeOffset ExpiresAt) CreateSession(string userId)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(SessionLifetimeDays);

        using var connection = database.Open();

        using (var cleanupCommand = connection.CreateCommand())
        {
            cleanupCommand.CommandText = "DELETE FROM sessions WHERE expires_at <= $now";
            cleanupCommand.Parameters.AddWithValue("$now", Timestamp(now));
            cleanupCommand.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (id, user_id, created_at, expires_at)
            VALUES ($id, $userId, $createdAt, $expiresAt)
            """;
        command.Parameters.AddWithValue("$id", HashToken(token));
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$createdAt", Timestamp(now));
        command.Parameters.AddWithValue("$expiresAt", Timestamp(expiresAt));
        command.ExecuteNonQuery();

        return (token, expiresAt);
    }

    public SessionUser? ResolveSession(string token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT users.id, users.username, users.is_admin
            FROM sessions
            JOIN users ON users.id = sessions.user_id
            WHERE sessions.id = $id AND sessions.expires_at > $now
            """;
        command.Parameters.AddWithValue("$id", HashToken(token));
        command.Parameters.AddWithValue("$now", Timestamp(DateTimeOffset.UtcNow));

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new SessionUser(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0)
            : null;
    }

    public void DeleteSession(string token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id = $id";
        command.Parameters.AddWithValue("$id", HashToken(token));
        command.ExecuteNonQuery();
    }

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private bool SetPassword(string userId, string newPassword, string? keepSessionTokenHash)
    {
        var passwordHash = passwordHasher.Hash(newPassword);

        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = "UPDATE users SET password_hash = $hash WHERE id = $id";
            updateCommand.Parameters.AddWithValue("$hash", passwordHash);
            updateCommand.Parameters.AddWithValue("$id", userId);

            if (updateCommand.ExecuteNonQuery() == 0)
            {
                return false;
            }
        }

        using (var deleteSessionsCommand = connection.CreateCommand())
        {
            deleteSessionsCommand.Transaction = transaction;
            if (keepSessionTokenHash is null)
            {
                deleteSessionsCommand.CommandText = "DELETE FROM sessions WHERE user_id = $userId";
                deleteSessionsCommand.Parameters.AddWithValue("$userId", userId);
            }
            else
            {
                deleteSessionsCommand.CommandText =
                    "DELETE FROM sessions WHERE user_id = $userId AND id <> $keepId";
                deleteSessionsCommand.Parameters.AddWithValue("$userId", userId);
                deleteSessionsCommand.Parameters.AddWithValue("$keepId", keepSessionTokenHash);
            }

            deleteSessionsCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    private static UserRecord? FindUser(
        SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction,
        string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$value", value);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new UserRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3) != 0,
            DateTimeOffset.Parse(
                reader.GetString(4),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None));
    }

    private static UserDto ReadUserDto(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2) != 0,
            DateTimeOffset.Parse(
                reader.GetString(3),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None));

    private static string Timestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
