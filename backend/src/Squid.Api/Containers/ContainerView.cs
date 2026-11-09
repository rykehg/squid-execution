using System.Text.Json;
using Squid.Domain;
using Squid.Domain.Containers;

namespace Squid.Api.Containers;

/// <summary>
/// The wire shape of contracts/http-api.md §4 (SPEC-0008 outputs: <c>ContainerView</c>). Policy
/// and membership enums travel as the contract's camelCase words — <c>membersOnly</c>,
/// <c>singlePublisher</c>, <c>owner</c> — never as the ordinals <c>Results.Json</c> would emit by
/// default, which is why every enum is rendered here instead of serialized raw.
/// </summary>
public static class ContainerView
{
    /// <summary>The container + policy body of <c>POST</c> and <c>GET</c> (contract §4).</summary>
    public static object Of(Container container) => Of(container, container.MemberCount);

    /// <summary>
    /// The same body with the count under the SPEC-0008 rule (SC-005): <c>memberCount</c> only
    /// appears to a reader who can already see the container, so a search hit out of their sight
    /// carries null — the field stays, the number does not leak.
    /// </summary>
    public static object Of(Container container, long? memberCount) => new
    {
        id = container.Id,
        slug = container.Slug,
        kind = Word(container.Kind),
        name = container.Name,
        summary = container.Summary,
        listed = container.Listed,
        policy = Policy(container.Policy),
        memberCount,
        createdAt = container.CreatedAt,
    };

    /// <summary>
    /// The policy half of the body above (contract §4), also the shape <c>PUT /policy</c> answers
    /// with and the <c>before</c>/<c>after</c> the audit trail records for a policy change
    /// (SPEC-0008 section B) — one rendering, so the three can never drift apart.
    /// </summary>
    public static object Policy(ContainerPolicy policy) => new
    {
        posting = Word(policy.Posting),
        publisherIds = policy.PublisherIds,
        visibility = Word(policy.Visibility),
        join = Word(policy.Join),
        interaction = new
        {
            canComment = Word(policy.Interaction.CanComment),
            canReact = Word(policy.Interaction.CanReact),
            canShare = Word(policy.Interaction.CanShare),
            canAttach = Word(policy.Interaction.CanAttach),
        },
        version = policy.Version,
    };

    /// <summary>One row of <c>GET /api/containers/{slug}/members</c> (contract §4).</summary>
    public static object Member(ContainerMembership member) => new
    {
        actorId = member.ActorId,
        handle = member.Handle,
        role = Word(member.Role),
        state = Word(member.State),
        invitedBy = member.InvitedBy,
        joinedAt = member.JoinedAt,
    };

    /// <summary>The contract's lowercase word for an enum value: <c>MembersOnly</c> → <c>membersOnly</c>.</summary>
    public static string Word<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
