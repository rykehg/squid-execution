using System.Text.RegularExpressions;
using Squid.Tests.Architecture;

namespace Squid.Tests.Quota;

/// <summary>
/// AC-06 of TASK-0028 (human gate 2026-11-10 08:00:15): <c>SkiaSharp.NativeAssets.Linux</c> is
/// DECLARED — versioned in <c>Directory.Packages.props</c> at the very version of SkiaSharp and
/// referenced by <c>Squid.Service.csproj</c> — because SkiaSharp itself ships Win32/macOS natives
/// only and CI ubuntu + compose would fail every decode with <c>DllNotFoundException</c>. This test
/// is static on purpose: executing a decode on Linux stays <c>unverified</c> on this Windows
/// machine, so only the declaration is asserted here.
/// </summary>
public sealed class LinuxNativeAssetsTests
{
    /// <summary>The version the human gate approved (AC-06): the SkiaSharp line, no other.</summary>
    private const string ApprovedVersion = "4.155.0";

    [Fact]
    public void Ac06_skia_sharp_linux_natives_are_declared_at_the_skia_sharp_version()
    {
        var backend = BackendRoot.Resolve();
        var props = File.ReadAllText(Path.Combine(backend, "Directory.Packages.props"));
        var csproj = File.ReadAllText(Path.Combine(backend, "src", "Squid.Service", "Squid.Service.csproj"));

        var skiaVersion = VersionOf(props, "SkiaSharp");
        var linuxVersion = VersionOf(props, "SkiaSharp.NativeAssets.Linux");

        Assert.Equal(ApprovedVersion, skiaVersion);
        Assert.Equal(skiaVersion, linuxVersion); // same version as SkiaSharp, never a different one (ADR-25)
        Assert.Contains(
            "<PackageReference Include=\"SkiaSharp.NativeAssets.Linux\" />",
            csproj,
            StringComparison.Ordinal);
    }

    /// <summary>The declared version of one package in the central versions file — or a loud failure.</summary>
    private static string VersionOf(string props, string package)
    {
        var match = Regex.Match(
            props,
            $"<PackageVersion Include=\"{Regex.Escape(package)}\" Version=\"([^\"]+)\" />",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"{package} não tem PackageVersion em Directory.Packages.props");
        return match.Groups[1].Value;
    }
}
