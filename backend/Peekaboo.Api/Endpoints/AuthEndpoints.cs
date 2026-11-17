using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Peekaboo.Api.Auth;

namespace Peekaboo.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", Login)
            .AllowAnonymous()
            .RequireRateLimiting(AuthConstants.LoginRateLimitPolicy);

        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", Me);
        group.MapPost("/password", ChangeOwnPassword);

        return app;
    }

    private static IResult Login(LoginRequest request, HttpContext context, AuthService auth)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length == 0 || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Username and password are required.");
        }

        var user = auth.Authenticate(username, request.Password);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid username or password.");
        }

        var (token, expiresAt) = auth.CreateSession(user.Id);
        context.Response.Cookies.Append(AuthConstants.CookieName, token, AuthCookies.Create(context, expiresAt));

        return Results.Ok(UserDto.From(user));
    }

    private static IResult Logout(HttpContext context, AuthService auth)
    {
        var token = context.Request.Cookies[AuthConstants.CookieName];
        if (!string.IsNullOrEmpty(token))
        {
            auth.DeleteSession(token);
        }

        context.Response.Cookies.Delete(AuthConstants.CookieName, AuthCookies.Create(context));
        return Results.NoContent();
    }

    private static IResult Me(HttpContext context, AuthService auth)
    {
        var userId = context.User.UserId();
        var user = userId is null ? null : auth.GetUser(userId);

        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static IResult ChangeOwnPassword(
        ChangePasswordRequest request,
        HttpContext context,
        AuthService auth)
    {
        var userId = context.User.UserId();
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var passwordError = AuthValidation.PasswordError(request.NewPassword);
        if (passwordError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: passwordError);
        }

        var token = context.Request.Cookies[AuthConstants.CookieName];
        var result = auth.ChangePassword(
            userId,
            request.CurrentPassword ?? string.Empty,
            request.NewPassword!,
            token is null ? null : AuthService.HashToken(token));

        return result switch
        {
            ChangePasswordResult.Changed => Results.NoContent(),
            ChangePasswordResult.WrongPassword => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Current password is incorrect."),
            _ => Results.Unauthorized(),
        };
    }
}
