using Microsoft.AspNetCore.Authorization;
using Peekaboo.Api.Auth;

namespace Peekaboo.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .RequireAuthorization(AuthConstants.AdminPolicy);

        group.MapGet("/", (AuthService auth) => Results.Ok(auth.ListUsers()));
        group.MapPost("/", CreateUser);
        group.MapDelete("/{id}", DeleteUser);
        group.MapPut("/{id}/password", ResetPassword);

        return app;
    }

    private static IResult CreateUser(CreateUserRequest request, AuthService auth)
    {
        var usernameError = AuthValidation.UsernameError(request.Username);
        if (usernameError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: usernameError);
        }

        var passwordError = AuthValidation.PasswordError(request.Password);
        if (passwordError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: passwordError);
        }

        var user = auth.TryCreateUser(request.Username!.Trim(), request.Password!, request.IsAdmin);
        if (user is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "That username is already taken.");
        }

        return Results.Created($"/api/users/{user.Id}", user);
    }

    private static IResult DeleteUser(string id, HttpContext context, AuthService auth)
    {
        var currentUserId = context.User.UserId();
        if (currentUserId is null)
        {
            return Results.Unauthorized();
        }

        return auth.DeleteUser(id, currentUserId) switch
        {
            DeleteUserResult.Deleted => Results.NoContent(),
            DeleteUserResult.NotFound => Results.NotFound(),
            DeleteUserResult.CannotDeleteSelf => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "You cannot delete your own account."),
            DeleteUserResult.LastAdmin => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "At least one administrator account must remain."),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static IResult ResetPassword(
        string id,
        ResetPasswordRequest request,
        HttpContext context,
        AuthService auth)
    {
        var passwordError = AuthValidation.PasswordError(request.NewPassword);
        if (passwordError is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: passwordError);
        }

        var currentUserId = context.User.UserId();
        var token = context.Request.Cookies[AuthConstants.CookieName];
        var keepSessionTokenHash = string.Equals(currentUserId, id, StringComparison.Ordinal) && token is not null
            ? AuthService.HashToken(token)
            : null;

        return auth.ResetPassword(id, request.NewPassword!, keepSessionTokenHash)
            ? Results.NoContent()
            : Results.NotFound();
    }
}
