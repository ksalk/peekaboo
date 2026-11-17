using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Auth;
using Peekaboo.Api.Data;
using Peekaboo.Api.Security;
using Xunit;

namespace Peekaboo.Tests.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private const string AdminPassword = "admin-password-123";

    private readonly string _directory =
        Directory.CreateTempSubdirectory("peekaboo-auth-tests-").FullName;

    private readonly AppDatabase _database;
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        _database = new AppDatabase(Path.Combine(_directory, "peekaboo.db"));
        _database.Initialize(NullLogger<AppDatabase>.Instance);
        _auth = new AuthService(_database, new PasswordHasher(), NullLogger<AuthService>.Instance);
    }

    [Fact]
    public void SeedAdmin_creates_admin_only_once()
    {
        _auth.SeedAdmin("admin", AdminPassword);
        _auth.SeedAdmin("other-admin", "other-password-123");

        var user = Assert.Single(_auth.ListUsers());
        Assert.Equal("admin", user.Username);
        Assert.True(user.IsAdmin);
    }

    [Fact]
    public void SeedAdmin_requires_credentials_when_no_users_exist()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _auth.SeedAdmin(null, null));

        Assert.Contains("ADMIN_USERNAME", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticate_accepts_valid_credentials_and_rejects_invalid()
    {
        _auth.SeedAdmin("admin", AdminPassword);

        Assert.NotNull(_auth.Authenticate("admin", AdminPassword));
        Assert.NotNull(_auth.Authenticate("ADMIN", AdminPassword));
        Assert.Null(_auth.Authenticate("admin", "wrong-password"));
        Assert.Null(_auth.Authenticate("nobody", AdminPassword));
    }

    [Fact]
    public void Session_lifecycle_works()
    {
        var admin = CreateAdmin();
        var (token, _) = _auth.CreateSession(admin.Id);

        var session = _auth.ResolveSession(token);
        Assert.NotNull(session);
        Assert.Equal(admin.Id, session.Id);
        Assert.True(session.IsAdmin);

        _auth.DeleteSession(token);
        Assert.Null(_auth.ResolveSession(token));
    }

    [Fact]
    public void Expired_session_is_rejected()
    {
        var admin = CreateAdmin();
        var (token, _) = _auth.CreateSession(admin.Id);

        using (var connection = _database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE sessions SET expires_at = $expiresAt WHERE id = $id";
            command.Parameters.AddWithValue("$expiresAt", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
            command.Parameters.AddWithValue("$id", AuthService.HashToken(token));
            command.ExecuteNonQuery();
        }

        Assert.Null(_auth.ResolveSession(token));
    }

    [Fact]
    public void Usernames_are_unique_case_insensitively()
    {
        _auth.SeedAdmin("admin", AdminPassword);

        Assert.NotNull(_auth.TryCreateUser("viewer", "viewer-password-123", isAdmin: false));
        Assert.Null(_auth.TryCreateUser("VIEWER", "other-password-123", isAdmin: false));
    }

    [Fact]
    public void Cannot_delete_self_last_admin_or_missing_user()
    {
        var admin = CreateAdmin();
        var viewer = _auth.TryCreateUser("viewer", "viewer-password-123", isAdmin: false)!;

        Assert.Equal(DeleteUserResult.CannotDeleteSelf, _auth.DeleteUser(admin.Id, admin.Id));
        Assert.Equal(DeleteUserResult.NotFound, _auth.DeleteUser("missing", admin.Id));
        Assert.Equal(DeleteUserResult.LastAdmin, _auth.DeleteUser(admin.Id, viewer.Id));
        Assert.Equal(DeleteUserResult.Deleted, _auth.DeleteUser(viewer.Id, admin.Id));
    }

    [Fact]
    public void Change_password_updates_hash_and_revokes_other_sessions()
    {
        var admin = CreateAdmin();
        var (keptToken, _) = _auth.CreateSession(admin.Id);
        var (revokedToken, _) = _auth.CreateSession(admin.Id);

        var result = _auth.ChangePassword(
            admin.Id,
            AdminPassword,
            "new-password-456",
            AuthService.HashToken(keptToken));

        Assert.Equal(ChangePasswordResult.Changed, result);
        Assert.Null(_auth.Authenticate("admin", AdminPassword));
        Assert.NotNull(_auth.Authenticate("admin", "new-password-456"));
        Assert.NotNull(_auth.ResolveSession(keptToken));
        Assert.Null(_auth.ResolveSession(revokedToken));
        Assert.Equal(
            ChangePasswordResult.WrongPassword,
            _auth.ChangePassword(admin.Id, "not-the-password", "another-password-789", null));
    }

    [Fact]
    public void Reset_password_revokes_all_sessions()
    {
        _auth.SeedAdmin("admin", AdminPassword);
        var viewer = _auth.TryCreateUser("viewer", "viewer-password-123", isAdmin: false)!;
        var (token, _) = _auth.CreateSession(viewer.Id);

        Assert.True(_auth.ResetPassword(viewer.Id, "reset-password-456", keepSessionTokenHash: null));
        Assert.Null(_auth.ResolveSession(token));
        Assert.NotNull(_auth.Authenticate("viewer", "reset-password-456"));
    }

    private UserRecord CreateAdmin()
    {
        _auth.SeedAdmin("admin", AdminPassword);
        return _auth.Authenticate("admin", AdminPassword)!;
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
