namespace Peekaboo.Api.Endpoints;

public sealed record LoginRequest(string? Username, string? Password);

public sealed record CreateUserRequest(string? Username, string? Password, bool IsAdmin);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record ResetPasswordRequest(string? NewPassword);

public sealed record NvrRequest(
    string? Name,
    string? BaseUrl,
    string? Transport,
    int? SdkPort);

public sealed record CameraRequest(string? Name, string? MainStreamId, string? SubStreamId);
