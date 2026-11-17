using System.Security.Claims;

namespace Peekaboo.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string? UserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);
}
