namespace Squid.Tests.Architecture;

/// <summary>
/// AC-01 and AC-02 of TASK-0002: the build has to refuse every edge the dependency rule forbids,
/// and keep accepting the legal ones. Each case is its own reported test, so
/// <c>dotnet test --filter Category=Architecture</c> names the edge that broke.
/// </summary>
[Trait("Category", "Architecture")]
[Trait("Category", "BuildProof")]   // each case copies the backend and spawns a dotnet build: ~10-25 s each
public sealed class DependencyRuleTests
{
    /// <summary>Edges that must be refused (AC-03, AC-04).</summary>
    public static TheoryData<string, string> ForbiddenEdges => new()
    {
        { "Squid.Domain", "Squid.Persistence" },
        { "Squid.Grains", "Squid.Persistence" },
        { "Squid.Persistence", "Squid.Grains" },
        { "Squid.ReadModels", "Squid.Grains" },
        { "Squid.Api", "Squid.Persistence" },
        { "Squid.Protocols", "Squid.Api" },
        { "Squid.Grains", "Squid.Service" },
        { "Squid.Domain", "Squid.Grains" },
    };

    /// <summary>Edges the same rule must keep accepting (AC-04, second half).</summary>
    public static TheoryData<string, string> AllowedEdges => new()
    {
        { "Squid.Grains", "Squid.Domain" },
        { "Squid.Api", "Squid.ReadModels" },
        { "Squid.Api", "Squid.Grains" },
        { "Squid.Service", "Squid.Persistence" },
        { "Squid.Service", "Squid.Worker" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenEdges))]
    public void BuildRefusesForbiddenEdge(string from, string to)
    {
        var outcome = BuildProbe.ForbiddenEdge(from, to);

        Assert.True(outcome.Refused,
            $"{from} -> {to} must be refused: build exited {outcome.ExitCode} "
            + $"({BuildProbe.RuleErrorCode} or MSB4006 expected). Output: {outcome.Tail()}");

        // The referenced project is named either way, so the diagnostic always points at the edge.
        Assert.Contains(to, outcome.Output, StringComparison.Ordinal);

        // When the edge closes a cycle, NuGet aborts the restore graph (MSB4006) before any build
        // target runs, and that message names only the referenced project — measured, never the
        // declaring one. Demanding `from` here would assert something MSBuild does not do, so the
        // declaring side is the one thing this refusal cannot be held to (BACKLOG S-08).
        if (outcome.RefusedByRule)
        {
            // The rule itself reports the pair, so both sides must be named for a complete diagnostic.
            Assert.Contains(from, outcome.Output, StringComparison.Ordinal);
            return;
        }

        Assert.True(outcome.RefusedAsCycle,
            $"{from} -> {to} was refused, but neither {BuildProbe.RuleErrorCode} nor MSB4006 appeared: {outcome.Tail()}");
    }

    [Theory]
    [MemberData(nameof(AllowedEdges))]
    public void BuildAcceptsAllowedEdge(string from, string to)
    {
        var outcome = BuildProbe.AllowedEdge(from, to);

        Assert.True(outcome.ExitCode == 0,
            $"{from} -> {to} must compile: build exited {outcome.ExitCode}. Output: {outcome.Tail()}");
    }
}