namespace Peekaboo.Api.Auth;

public static class AuthCookies
{
    public static CookieOptions Create(HttpContext context, DateTimeOffset? expiresAt = null) =>
        new()
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
            IsEssential = true,
        };
}
