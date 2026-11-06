using System.Diagnostics;
using System.Text;

namespace Squid.Tests.Architecture;

/// <summary>
/// Proves a dependency edge by building it. The backend is copied to a temporary folder, one
/// <c>ProjectReference</c> is injected, and that project is built there; the repository is never
/// modified, so a proof cannot leave a violation behind. The copy carries sources and project
/// files only, never <c>bin/</c> or <c>obj/</c>, because those record absolute paths and would be
/// reused instead of rebuilt.
/// </summary>
internal static class BuildProbe
{
    /// <summary>Error code emitted by backend/Directory.Build.targets.</summary>
    public const string RuleErrorCode = "SQUID0001";

    /// <summary>
    /// One <c>dotnet build</c> of <paramref name="from"/> with an injected reference to
    /// <paramref name="to"/>. <c>BuildSucceeded</c> is the proof of an allowed edge; <c>Refused</c>
    /// is the proof of a forbidden one.
    /// </summary>
    public sealed record Outcome(int ExitCode, bool Injected, string Output)
    {
        /// <summary>True when the rule refused the edge: it named it (SQUID0001) or the reference closed a cycle (MSB4006).</summary>
        public bool Refused => ExitCode != 0
            && (Output.Contains(RuleErrorCode, StringComparison.Ordinal)
                || Output.Contains("MSB4006", StringComparison.Ordinal));

        /// <summary>
        /// True when the refusal came from the dependency rule itself, naming the edge.
        /// </summary>
        public bool RefusedByRule => ExitCode != 0 && Output.Contains(RuleErrorCode, StringComparison.Ordinal);

        /// <summary>
        /// True when the edge closes a cycle, so NuGet aborts the restore graph (MSB4006) before any
        /// build target runs. Measured, not assumed: no MSBuild target can fire earlier, because the
        /// cycle is detected while the restore graph is built and item lists cannot be read at
        /// evaluation time. In that case MSB4006 is the refusal, and it names the referenced project.
        /// </summary>
        public bool RefusedAsCycle => ExitCode != 0 && Output.Contains("MSB4006", StringComparison.Ordinal);

        /// <summary>The last lines of the build, so a failure report says why.</summary>
        public string Tail() => string.Join(" | ", Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(6));
    }

    /// <summary>
    /// One <c>dotnet build</c> of <paramref name="from"/> with an injected reference to
    /// <paramref name="to"/>, for a case that is forbidden. <c>Refused</c> is the proof: the rule
    /// refused the edge.
    /// </summary>
    public static Outcome ForbiddenEdge(string from, string to) => Run(from, to, expectSuccess: false);

    /// <summary>
    /// One <c>dotnet build</c> of <paramref name="from"/> with an injected reference to
    /// <paramref name="to"/>, for a case the rule must accept. Only a build that ran to the end
    /// proves the edge accepted — a build that died on its environment proves nothing.
    /// </summary>
    public static Outcome AllowedEdge(string from, string to) => Run(from, to, expectSuccess: true);

    private static Outcome Run(string from, string to, bool expectSuccess)
    {
        var source = BackendRoot.Resolve();
        var work = Path.Combine(Path.GetTempPath(), "squid-deprule-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyProjectTree(source, work);

            var projectFile = Path.Combine(work, "src", from, $"{from}.csproj");
            var injected = AddProjectReference(projectFile, to);

            // An allowed edge may already exist in the real graph (Squid.Grains -> Squid.Domain):
            // proving it again would only re-build the tree, so the proof is read from the tree.
            if (expectSuccess && !injected) return new Outcome(0, injected, "edge already declared in the backend");

            var (exitCode, output) = Build(projectFile);
            return new Outcome(exitCode, injected, output);
        }
        finally
        {
            TryDelete(work);
        }
    }

    /// <summary>Adds the edge unless it is already declared.</summary>
    private static bool AddProjectReference(string projectFile, string to)
    {
        var text = File.ReadAllText(projectFile);
        if (text.Contains($"..\\{to}\\{to}.csproj", StringComparison.Ordinal)) return false;

        var block = $"  <ItemGroup>\n    <ProjectReference Include=\"..\\{to}\\{to}.csproj\" />\n  </ItemGroup>\n\n</Project>";
        var modified = text.Replace("</Project>", block, StringComparison.Ordinal);
        Assert.True(modified != text, $"Could not inject the reference to {to} into {projectFile}.");
        File.WriteAllText(projectFile, modified, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static void CopyProjectTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var root in new[] { "src", "tests" })
        {
            var from = Path.Combine(source, root);
            if (Directory.Exists(from)) CopyInto(from, Path.Combine(destination, root));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        // The copy restores from here, and it has to restore offline: nuget.config pins the package
        // folder to backend/.packages, which lives outside the copy. Without it the copied projects
        // go to the default cache, where the test packages may be absent. A directory junction needs
        // no elevation (a symbolic link does) and keeps the copy from paying for a second copy.
        var packages = Path.Combine(source, ".packages");
        var link = Path.Combine(destination, ".packages");
        if (Directory.Exists(packages) && !Directory.Exists(link))
        {
            var start = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link);
            start.ArgumentList.Add(packages);

            using var junction = Process.Start(start)!;
            var diag = junction.StandardOutput.ReadToEnd() + junction.StandardError.ReadToEnd();
            junction.WaitForExit();

            // Not fatal: without the link the copied build falls back to the default package cache.
            if (!Directory.Exists(link))
            {
                Console.WriteLine($"[BuildProbe] could not link the package cache into the temp copy: {diag.Trim()}");
            }
        }
    }

    private static void CopyInto(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            if (Path.GetFileName(directory) is "bin" or "obj") continue;
            CopyInto(directory, Path.Combine(to, Path.GetFileName(directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static (int ExitCode, string Output) Build(string project)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(project)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-v:m");
        start.ArgumentList.Add("-m:1");
        UseWritableDotnetHome(start);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start 'dotnet'. Is the SDK on PATH?");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }

    /// <summary>
    /// The .NET CLI writes first-run sentinels under DOTNET_CLI_HOME. When the user profile folder
    /// is denied (confined session), every <c>dotnet</c> call dies with UnauthorizedAccessException
    /// before compiling, and a "build refused" probe would pass for the wrong reason — or fail for
    /// one. The child gets a home inside the repository, which is writable wherever the build ran.
    /// </summary>
    private static void UseWritableDotnetHome(ProcessStartInfo start)
    {
        // Environment[...] throws for a missing key, so read it through the lookup that does not.
        if (start.Environment.TryGetValue("DOTNET_CLI_HOME", out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            return;
        }

        var home = Path.GetFullPath(Path.Combine(BackendRoot.Resolve(), "..", ".dotnet-home"));
        Directory.CreateDirectory(home);
        start.Environment["DOTNET_CLI_HOME"] = home;

        if (!start.Environment.TryGetValue("NUGET_PACKAGES", out var packages) || string.IsNullOrWhiteSpace(packages))
        {
            start.Environment["NUGET_PACKAGES"] = Path.Combine(home, "nuget");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;

            // Read-only bits from the copied build output would otherwise block the delete.
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp folder is not a product failure; the OS cleans it up.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: best effort.
        }
    }
}

/// <summary>Path of a project inside the backend, resolved from this assembly's output folder.</summary>
internal static class BackendRoot
{
    /// <summary>
    /// Walks up from <c>backend/tests/Squid.Tests/bin/&lt;config&gt;/&lt;tfm&gt;</c> to <c>backend/</c>.
    /// </summary>
    public static string Resolve()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Squid.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find backend/Squid.slnx above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Path of a project directory under the backend.</summary>
    public static string ProjectDirectory(string projectName)
    {
        var root = Resolve();
        var candidate = Path.Combine(root, "src", projectName);
        return Directory.Exists(candidate) ? candidate : Path.Combine(root, "tests", projectName);
    }
}