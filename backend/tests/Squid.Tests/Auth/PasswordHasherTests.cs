using Squid.Api.Auth;

namespace Squid.Tests.Auth;

/// <summary>B-04 of SPEC-0007: a login password exists only as an Argon2id hash, never in clear.</summary>
public sealed class PasswordHasherTests
{
    private const string Password = "senha-forte-1234";

    [Fact]
    public void Hash_is_argon2id_in_the_phc_form_and_carries_no_password()
    {
        var hash = PasswordHasher.Hash(Password);

        Assert.StartsWith("$argon2id$v=19$m=", hash, StringComparison.Ordinal);
        Assert.Contains("t=3", hash, StringComparison.Ordinal);   // parameters travel with the hash
        Assert.Contains("p=1", hash, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_accepts_the_password_it_hashed_and_refuses_any_other()
    {
        var hash = PasswordHasher.Hash(Password);

        Assert.True(PasswordHasher.Verify(Password, hash));
        Assert.False(PasswordHasher.Verify("senha-diferente-99", hash));
        Assert.False(PasswordHasher.Verify(string.Empty, hash));
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        var first = PasswordHasher.Hash(Password);
        var second = PasswordHasher.Hash(Password);

        Assert.NotEqual(first, second); // a fresh salt per hash
        Assert.True(PasswordHasher.Verify(Password, first));
        Assert.True(PasswordHasher.Verify(Password, second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("$2y$10$notArgon2idAtAll")]
    [InlineData("$argon2id$v=19$m=abc,t=3,p=1$c2FsdA==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=4096,t=3,p=1$not-base-64$aGFzaA==")]
    public void A_malformed_hash_refuses_the_login_instead_of_throwing(string stored)
    {
        Assert.False(PasswordHasher.Verify(Password, stored));
    }

    [Fact]
    public void The_timing_dummy_never_accepts_a_login()
    {
        // No account row ever holds it; it only makes the unknown-handle run cost one Argon2id too.
        Assert.False(PasswordHasher.Verify(Password, PasswordHasher.DummyHash));
        Assert.False(PasswordHasher.Verify(string.Empty, PasswordHasher.DummyHash));
    }
}
