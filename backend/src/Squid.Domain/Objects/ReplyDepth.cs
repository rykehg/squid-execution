namespace Squid.Domain.Objects;

/// <summary>
/// The chain depth rule of data-model §2.5 (<c>ReplyToId</c>: comentário encadena, profundidade
/// ≤ 3 no MVP) and contract §5. The root post sits at depth 0 and its direct answers at depth 1;
/// SPEC-0009 section A with AC-05 fix the boundary on that same scale — depth 3 publishes, the
/// level above it is refused with <c>422 invalid_reply_target</c>.
/// </summary>
public static class ReplyDepth
{
    /// <summary>The deepest publishable answer (§2.5, contract §5).</summary>
    public const int MaxDepth = 3;

    /// <summary>The depth an answer to a post at <paramref name="parentDepth"/> would take.</summary>
    public static int DepthOf(int parentDepth) => parentDepth + 1;

    /// <summary>Whether an answer to a post at <paramref name="parentDepth"/> may be published.</summary>
    public static bool AllowedFor(int parentDepth) => parentDepth >= 0 && DepthOf(parentDepth) <= MaxDepth;
}
