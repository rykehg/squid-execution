using System.Globalization;
using Squid.Tests.Auth;

namespace Squid.Tests.Stream;

/// <summary>
/// TASK-0026 AC-04 — the delivery-time gate: a post of a container the subscriber cannot see
/// (a non-member of a <c>MembersOnly</c>, a member the container banned) NEVER reaches the
/// stream. Absence is proven the deterministic way — publish the forbidden post FIRST, then a
/// control post the subscriber does see, and read everything up to the control: had the filter
/// let the forbidden one through, it would be sitting right there in the frames read.
/// </summary>
[Collection("ServiceHost")]
public sealed class StreamVisibilityTests : IClassFixture<StreamHostFixture>
{
    private readonly StreamHostFixture _host;

    public StreamVisibilityTests(StreamHostFixture host) => _host = host;

    [Fact] // AC-04 — não-membro em MembersOnly
    public async Task Ac04_a_post_of_a_container_the_subscriber_does_not_see_never_reaches_the_stream()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "svmemdono");
        var (outsider, _) = await StreamHarness.SignedInAsync(_host, "svmemfora");
        var restrictedSlug = await StreamHarness.NewContainerAsync(owner, "svmembro");
        await StreamHarness.SetVisibilityAsync(owner, restrictedSlug, "membersonly");
        var openSlug = await StreamHarness.NewContainerAsync(owner, "svaberto");

        using var stream = await SseConnection.OpenAsync(_host, outsider);

        // The forbidden post is written FIRST: its seq precedes the control's on the wire.
        var hiddenContent = StreamHarness.Unique("segredo");
        var hiddenPostId = await StreamHarness.PublishAsync(owner, restrictedSlug, hiddenContent);
        var shownContent = StreamHarness.Unique("publico");
        var shownPostId = await StreamHarness.PublishAsync(owner, openSlug, shownContent);

        var seen = await stream.ReadUntilAsync(Matches(shownContent), TimeSpan.FromSeconds(5));
        var control = Assert.Single(seen, Matches(shownContent));
        Assert.DoesNotContain(seen, Matches(hiddenContent));

        // The premise of the proof: the hidden post really was in line before the control.
        var hiddenSeq = await StreamHarness.GlobalSeqAsync(_host, hiddenPostId);
        var shownSeq = await StreamHarness.GlobalSeqAsync(_host, shownPostId);
        Assert.True(hiddenSeq < shownSeq, "the forbidden post must precede the control to prove the gate");
        Assert.True(long.Parse(control.Id!, CultureInfo.InvariantCulture) == shownSeq,
            "the control frame carries its own global seq");
    }

    [Fact] // AC-04 — Banned
    public async Task Ac04_a_banned_member_never_receives_the_posts_of_the_container_that_banned_them()
    {
        var (owner, _) = await StreamHarness.SignedInAsync(_host, "svbandono");
        var (member, memberHandle) = await StreamHarness.SignedInAsync(_host, "svbanmembro");
        var slug = await StreamHarness.NewContainerAsync(owner, "svban");
        await StreamHarness.JoinAsync(member, slug);
        await StreamHarness.BanAsync(owner, slug, memberHandle);
        var openSlug = await StreamHarness.NewContainerAsync(owner, "svbanlivre");

        using var stream = await SseConnection.OpenAsync(_host, member);

        var bannedContent = StreamHarness.Unique("banido");
        var bannedPostId = await StreamHarness.PublishAsync(owner, slug, bannedContent);
        var shownContent = StreamHarness.Unique("livre");
        var shownPostId = await StreamHarness.PublishAsync(owner, openSlug, shownContent);

        var seen = await stream.ReadUntilAsync(Matches(shownContent), TimeSpan.FromSeconds(5));
        Assert.Single(seen, Matches(shownContent));
        Assert.DoesNotContain(seen, Matches(bannedContent));

        // Same premise: the banned container's post was in line first — the subscriber is simply
        // not entitled to it, while an unrelated public container keeps flowing (membership is
        // per container, the ban does not leak).
        var bannedSeq = await StreamHarness.GlobalSeqAsync(_host, bannedPostId);
        var shownSeq = await StreamHarness.GlobalSeqAsync(_host, shownPostId);
        Assert.True(bannedSeq < shownSeq, "the banned post must precede the control to prove the gate");
    }

    /// <summary>The frame carrying one unique content (pings and foreign backlog never match).</summary>
    private static Predicate<SseFrame> Matches(string content) =>
        frame => frame.Data is not null && frame.Data.Contains(content, StringComparison.Ordinal);
}
