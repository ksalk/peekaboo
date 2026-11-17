namespace Peekaboo.Api.Auth;

public sealed record UserRecord(
    string Id,
    string Username,
    string PasswordHash,
    bool IsAdmin,
    DateTimeOffset CreatedAt);

public sealed record SessionUser(string Id, string Username, bool IsAdmin);

public sealed record UserDto(string Id, string Username, bool IsAdmin, DateTimeOffset CreatedAt)
{
    public static UserDto From(UserRecord user) =>
        new(user.Id, user.Username, user.IsAdmin, user.CreatedAt);
}

public enum DeleteUserResult
{
    Deleted,
    NotFound,
    CannotDeleteSelf,
    LastAdmin,
}

public enum ChangePasswordResult
{
    Changed,
    NotFound,
    WrongPassword,
}
