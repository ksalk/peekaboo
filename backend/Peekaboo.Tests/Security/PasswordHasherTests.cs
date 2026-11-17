using Peekaboo.Api.Security;
using Xunit;

namespace Peekaboo.Tests.Security;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_succeeds()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Hash_uses_unique_salt()
    {
        var first = _hasher.Hash("same password");
        var second = _hasher.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.True(_hasher.Verify("same password", first));
        Assert.True(_hasher.Verify("same password", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$abc$salt$hash")]
    [InlineData("pbkdf2-sha256$1000$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$210000$not-base64$not-base64")]
    [InlineData("other-scheme$210000$c2FsdA==$aGFzaA==")]
    public void Verify_rejects_malformed_hashes(string encodedHash)
    {
        Assert.False(_hasher.Verify("password", encodedHash));
    }
}
