using System.Xml.Linq;

namespace Squid.Tests.Architecture;

/// <summary>
/// AC-02 of TASK-0002: <c>Squid.Domain</c> carries no Orleans and no JSON-LD, and takes its
/// target framework from the shared build props. The checks read the project files, which is
/// where a reference is declared, so they need nothing installed and no network.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class DomainHasNoInfrastructureTests
{
    /// <summary>Package prefixes that must never appear in the canonical model.</summary>
    private static readonly string[] ForbiddenPackagePrefixes =
    [
        "Microsoft.Orleans",  // the actor framework: only the grains layer knows it (ADR-1)
        "Orleans",            // the same framework under its short name
        "JsonLd",             // JSON-LD is the adapter's vocabulary, never the domain's (ADR-11)
        "Newtonsoft.Json",    // only ever pulled in today by JSON-LD tooling
    ];

    /// <summary>Project references that would drag infrastructure back into the domain.</summary>
    private static readonly string[] ForbiddenProjectReferences =
    [
        "Squid.Grains",
        "Squid.Persistence",
        "Squid.Protocols",
        "Squid.ReadModels",
        "Squid.Api",
        "Squid.Service",
        "Squid.Worker",
    ];

    /// <summary>Target framework the whole backend builds for (backend/Directory.Build.props).</summary>
    private const string ExpectedTargetFramework = "net10.0";

    [Fact]
    public void DomainDoesNotReferenceOrleansOrJsonLd()
    {
        var document = XDocument.Load(DomainProjectFile());
        var packages = ReferenceNames(document, "PackageReference");

        Assert.DoesNotContain(packages, name => ForbiddenPackagePrefixes.Any(prefix =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(packages);
    }

    [Fact]
    public void DomainDoesNotReferenceInfrastructureProjects()
    {
        var document = XDocument.Load(DomainProjectFile());
        var references = ReferenceNames(document, "ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference))
            .ToList();

        Assert.DoesNotContain(references, name => ForbiddenProjectReferences.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void DomainProjectTargetsNet10()
    {
        var projectFile = DomainProjectFile();
        var ownTarget = TargetFramework(XDocument.Load(projectFile));

        // The project must not pin its own framework: it inherits the shared one.
        Assert.Null(ownTarget);

        var shared = Path.Combine(BackendRoot.Resolve(), "Directory.Build.props");
        Assert.Equal(ExpectedTargetFramework, TargetFramework(XDocument.Load(shared)));
    }

    private static string DomainProjectFile()
    {
        var path = Path.Combine(BackendRoot.ProjectDirectory("Squid.Domain"), "Squid.Domain.csproj");
        Assert.True(File.Exists(path), $"Squid.Domain.csproj not found at '{path}'.");
        return path;
    }

    private static List<string> ReferenceNames(XDocument document, string elementName) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == elementName)
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToList();

    private static string? TargetFramework(XDocument document) =>
        document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "TargetFramework")
            ?.Value.Trim();
}