using Squid.Domain.Objects;

namespace Squid.Api.Posts;

/// <summary>
/// <c>Actor.Status</c> (data-model §3.1) at the publish ladder of SPEC-0009 section A: a
/// <c>Suspended</c> actor does not publish — refused and audited, while reading keeps working.
/// The status has no route yet: TASK-0031 births <c>POST /api/admin/actors/{id}/suspend</c> on
/// top of this same store, so the rule is already active from the first publish of P-04 with
/// nothing to wait for. An actor with no row is <see cref="ActorStatus.Active"/>.
/// </summary>
public interface IActorStatusStore
{
    /// <summary>The status of <paramref name="handle"/>; no row means the live state (§3.1).</summary>
    Task<ActorStatus> GetAsync(string handle);

    /// <summary>Moves <paramref name="handle"/> to <see cref="ActorStatus.Suspended"/>; repeating it changes nothing.</summary>
    Task SuspendAsync(string handle);
}
