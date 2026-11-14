using Microsoft.AspNetCore.Http;

namespace Squid.Api.Federation;

/// <summary>
/// The inbox pipeline of SPEC-0011 section E (contract activitypub §3, FR-040): the port the two
/// <c>POST .../inbox</c> routes run on. Declared here, on the edge (ADR-35), implemented in
/// <c>Squid.Service</c> — the only layer that can hold an adapter, a grain, the stores and an
/// <c>HttpContext</c> at once. It answers a verdict; this assembly keeps the status codes, the
/// error envelope and the media types, exactly as it does for every other ladder.
/// </summary>
public interface IInboxPipeline
{
    /// <summary>
    /// Runs contract §3 in its fixed order over one inbox delivery — signature, digest, date,
    /// size, depth, rate limit, dedupe, sender — and applies the activity when every step held.
    /// Never throws on hostile input: every failure is an <see cref="InboxVerdict"/> (FR-040).
    /// </summary>
    Task<InboxVerdict> ReceiveAsync(HttpContext context, InboxTarget target, CancellationToken cancellationToken);
}

/// <summary>
/// The inbox the delivery arrived at (SPEC-0011 section E): the local actor of a
/// <c>/users/{name}/inbox</c>, the local container of a <c>/c/{slug}/inbox</c> — the two are
/// never both set, so a query string can never stand in for the route that was actually hit.
/// </summary>
public readonly record struct InboxTarget(string? Handle, string? ContainerSlug)
{
    /// <summary>The actor inbox route.</summary>
    public static InboxTarget ForActor(string name) => new(name, null);

    /// <summary>The container inbox route.</summary>
    public static InboxTarget ForContainer(string slug) => new(null, slug);
}

/// <summary>Where contract §3 stopped (each word maps to the status the contract fixes).</summary>
public enum InboxOutcome
{
    /// <summary>Every step held; the activity was applied, or deliberately left unapplied (202).</summary>
    Accepted,

    /// <summary>Step 1: absent or unverifiable signature (401).</summary>
    SignatureInvalid,

    /// <summary>Step 2: the Digest of the body does not confer (401).</summary>
    DigestMismatch,

    /// <summary>Step 3: no Date at all, or one outside ±5 min (401).</summary>
    StaleDate,

    /// <summary>Step 4: a body above 1 MB (413).</summary>
    PayloadTooLarge,

    /// <summary>Step 4: nesting past 32 levels (400).</summary>
    PayloadTooDeep,

    /// <summary>The activity is not one this pipeline can act on structurally — no id, no actor (400).</summary>
    InvalidActivity,

    /// <summary>Step 5: the domain is above its per-minute quota (429 + Retry-After).</summary>
    RateLimited,

    /// <summary>Step 7: the sender does not resolve to any published actor (401).</summary>
    SenderUnresolvable,
}

/// <summary>The answer of one inbox delivery: the step it stopped at, plus what a 429 or a Follow needs.</summary>
/// <param name="Outcome">Where §3 stopped; <see cref="InboxOutcome.Accepted"/> is always a 202.</param>
/// <param name="RetryAfterSeconds">Seconds a 429 puts in its Retry-After header.</param>
/// <param name="ActivityJson">The activity to answer with — the Accept a Follow owes (AC-05), null otherwise.</param>
public sealed record InboxVerdict(
    InboxOutcome Outcome,
    string? RetryAfterSeconds = null,
    string? ActivityJson = null);
