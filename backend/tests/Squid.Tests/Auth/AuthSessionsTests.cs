using Squid.Api.Auth;

namespace Squid.Tests.Auth;

/// <summary>B-05/B-06 mechanics the HTTP tests cannot reach: the slide, the cookie value and the
/// fixed-time CSRF comparison.</summary>
public sealed class AuthSessionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_session_lives_thirty_days_and_the_slide_renews_only_the_one_in_use()
    {
        Assert.Equal(TimeSpan.FromDays(30), AuthSessions.Lifetime);

        var used = new SessionRecord("used", "csrf-a", Now.AddDays(-29), Now.AddDays(-1));
        var untouched = new SessionRecord("other", "csrf-b", Now.AddDays(-10), Now.AddDays(20));

        var slid = AuthSessions.Slide([used, untouched], "used", Now);

        Assert.Equal(2, slid.Count);
        Assert.Equal(Now.AddDays(30), slid[0].ExpiresAt);
        Assert.Equal(untouched.ExpiresAt, slid[1].ExpiresAt);
    }

    [Fact]
    public void The_cookie_carries_handle_and_id_and_a_malformed_one_is_no_session()
    {
        var value = AuthSessions.CookieValue("alice", "a1b2c3");

        var parsed = AuthSessions.ParseCookie(value);
        Assert.NotNull(parsed);
        Assert.Equal(("alice", "a1b2c3"), parsed.Value);

        Assert.Null(AuthSessions.ParseCookie(null));
        Assert.Null(AuthSessions.ParseCookie(string.Empty));
        Assert.Null(AuthSessions.ParseCookie("nodot"));
        Assert.Null(AuthSessions.ParseCookie(".leading"));
        Assert.Null(AuthSessions.ParseCookie("trailing."));
    }

    [Fact]
    public void The_csrf_header_has_to_carry_the_session_token_exactly()
    {
        Assert.True(AuthSessions.CsrfMatches("a-token", "a-token"));
        Assert.False(AuthSessions.CsrfMatches("a-token", "a-token-x"));
        Assert.False(AuthSessions.CsrfMatches("wrong", "a-token"));
        Assert.False(AuthSessions.CsrfMatches(string.Empty, "a-token"));
        Assert.False(AuthSessions.CsrfMatches(null, "a-token"));
    }

    [Fact]
    public void Issued_tokens_are_opaque_hex_and_unique()
    {
        var first = AuthSessions.Issue(Now);
        var second = AuthSessions.Issue(Now);

        Assert.Equal(Now.Add(AuthSessions.Lifetime), first.ExpiresAt);
        Assert.Equal(48, first.Id.Length);          // 24 bytes
        Assert.Equal(64, first.CsrfToken.Length);   // 32 bytes
        Assert.Matches("^[0-9a-f]+$", first.Id);    // hex: a token can never spell "kit"
        Assert.Matches("^[0-9a-f]+$", first.CsrfToken);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.CsrfToken, second.CsrfToken);
    }
}
