using Squid.Domain.Objects;

namespace Squid.Tests.Posts;

/// <summary>
/// The pure rules of <c>Squid.Domain/Objects</c> behind AC-05 and AC-06: GAP-24's sanitization
/// (escape HTML, drop C0 controls, keep the text's own whitespace) and the reply depth rule of
/// data-model §2.5 (depth 3 publishes, the level above it does not). No host: the domain is
/// package-free and I/O-free by build rule (SQUID0001), so these are milliseconds.
/// </summary>
public sealed class PostContentRulesTests
{
    [Fact] // AC-06, first half
    public void Sanitize_escapes_html_and_drops_c0_controls_while_the_text_keeps_its_whitespace()
    {
        var clean = PostSanitizer.Sanitize("<script>alert(1)</script> oi& tá ok\r\nnova linha\ttab\x07\x00\x7F");

        Assert.DoesNotContain("<script>", clean, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", clean, StringComparison.Ordinal);
        Assert.Contains("&amp;", clean, StringComparison.Ordinal); // the ampersand is text now, not markup
        Assert.DoesNotContain("\x07", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("\x00", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("\x7F", clean, StringComparison.Ordinal);
        Assert.Contains("\r\nnova linha\ttab", clean, StringComparison.Ordinal); // CR/LF/tab survive
    }

    [Fact]
    public void Sanitize_never_leaves_a_tag_behind_nor_invents_content()
    {
        var clean = PostSanitizer.Sanitize("<b>não</b> <img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<", clean, StringComparison.Ordinal);
        Assert.DoesNotContain(">", clean, StringComparison.Ordinal);
        Assert.Contains("não", clean, StringComparison.Ordinal);
        Assert.Contains("onerror=alert(1)", clean, StringComparison.Ordinal); // escaped, not deleted: the text stays readable
    }

    [Fact] // AC-06, the cap itself
    public void The_content_cap_is_the_data_model_one()
    {
        Assert.Equal(5000, PostObject.MaxContentLength);
    }

    [Fact] // AC-05, the rule behind 422
    public void Depth_counts_from_the_root_so_three_answers_publish_and_the_fourth_level_does_not()
    {
        Assert.Equal(1, ReplyDepth.DepthOf(0)); // a direct answer to the root sits at depth 1
        Assert.Equal(3, ReplyDepth.DepthOf(2));
        Assert.True(ReplyDepth.AllowedFor(0));  // direct answer: depth 1
        Assert.True(ReplyDepth.AllowedFor(2));  // depth 3 — "profundidade 3 funciona"
        Assert.False(ReplyDepth.AllowedFor(3)); // would be depth 4 — the level above the maximum
        Assert.False(ReplyDepth.AllowedFor(-1)); // a negative parent is not a post at all
        Assert.Equal(3, ReplyDepth.MaxDepth);
    }
}
