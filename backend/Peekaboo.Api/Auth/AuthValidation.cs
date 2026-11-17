using System.Text.RegularExpressions;

namespace Peekaboo.Api.Auth;

public static partial class AuthValidation
{
    [GeneratedRegex("^[A-Za-z0-9._-]{3,64}$")]
    private static partial Regex UsernamePattern();

    public static string? UsernameError(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "Username is required.";
        }

        return UsernamePattern().IsMatch(username.Trim())
            ? null
            : "Username must be 3-64 characters using letters, digits, dot, underscore, or hyphen.";
    }

    public static string? PasswordError(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Password is required.";
        }

        return password.Length is < 8 or > 256
            ? "Password must be 8-256 characters."
            : null;
    }
}
