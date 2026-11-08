using System.Text.Json.Serialization;

namespace Squid.Grains.Actors;

/// <summary>
/// Event of the actor log (SPEC-0007 B-08). The discriminator travels inside the persisted JSON,
/// so a log still replays after the snapshot truncates the events it already covers.
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(ActorCreated), typeDiscriminator: "created")]
[JsonDerivedType(typeof(ActorDisplayNameChanged), typeDiscriminator: "display-name-changed")]
public abstract record ActorEvent;

/// <summary>
/// First event of every stream: the handle has been validated and found free before it is
/// written, so a stream always starts with exactly one of these (AC-01, AC-02).
/// </summary>
public sealed record ActorCreated(string Handle, string? DisplayName, ActorKeyMaterial Keys) : ActorEvent;

/// <summary>
/// Display name change — the repeatable event of the actor stream. It exists so a log can hold
/// more events than it takes to cross the snapshot threshold, which is what AC-03 and AC-04 test.
/// </summary>
public sealed record ActorDisplayNameChanged(string DisplayName) : ActorEvent;
