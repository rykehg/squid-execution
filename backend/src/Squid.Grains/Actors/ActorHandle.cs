using System.Text.RegularExpressions;

namespace Squid.Grains.Actors;

/// <summary>
/// Local handle rule of SPEC-0007 B-01: <c>[a-z0-9_]{3,30}</c>, unique in the domain and
/// immutable once the actor exists.
/// </summary>
public static class ActorHandle
{
    /// <summary>The rule exactly as the API has to cite it in the rejection message (422).</summary>
    public const string Rule = "[a-z0-9_]{3,30}";

    // \A..\z, not ^..$: `$` accepts a trailing newline, a handle never carries one.
    private static readonly Regex Pattern = new(
        $@"\A{Rule}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True only when <paramref name="handle"/> matches the whole rule.</summary>
    public static bool IsValid(string? handle) => handle is not null && Pattern.IsMatch(handle);
}
