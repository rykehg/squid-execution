using System.Text.RegularExpressions;

namespace Squid.Domain.Containers;

/// <summary>
/// The slug rule of the container route (contract §4): it is the path segment of the canonical
/// id, so it accepts only URL-safe characters, is validated once at creation and never changes
/// afterwards (data-model §2.2 — slug imutável).
/// </summary>
public static class ContainerSlug
{
    /// <summary>The rule stated verbatim in the 400 message, the way <c>ActorHandle.Rule</c> does.</summary>
    public const string Rule =
        "1 a 64 caracteres de letras, dígitos, ponto, hífen ou sublinhado, "
        + "começando e terminando com letra ou dígito";

    private static readonly Regex Pattern = new(
        "^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True when <paramref name="slug"/> is a valid container path segment.</summary>
    public static bool IsValid(string? slug) => slug is not null && Pattern.IsMatch(slug);
}
