using System.Text.RegularExpressions;
using Squid.Tests.Architecture;

namespace Squid.Tests.Storage;

/// <summary>
/// AC-06 of TASK-0003: a scripted grep over the <c>Squid.Persistence</c> sources must find no
/// store name as a literal — the store is always a parameter (doc 05 §4.7 #2). The scan passes
/// with zero hits and fails listing file:line for each one.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class StoreNameLiteralTests
{
    // Store names the product declares today (AC-03); every new store joins this list.
    private static readonly Regex StoreNameLiteral = new(
        @"\b(?:actors?|containers?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void SquidPersistenceSourcesDeclareNoStoreNameLiteral()
    {
        var projectDir = BackendRoot.ProjectDirectory("Squid.Persistence");
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(projectDir, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (StoreNameLiteral.IsMatch(line))
                {
                    hits.Add($"{relative}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Store name as literal in Squid.Persistence (the store must be a parameter):"
            + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }
}
