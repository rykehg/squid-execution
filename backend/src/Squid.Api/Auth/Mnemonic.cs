using System.Security.Cryptography;

namespace Squid.Api.Auth;

/// <summary>
/// The 24 words of the recovery kit (SPEC-0007 B-12, FR-005). The words are re-derivable from the
/// `RC` the instance keeps, because `POST /api/recovery/kit` has to hand out the same mnemonic it
/// already issued — but the RC itself is a 256-bit credential, so the words carry
/// <c>SHA-256(RC)[0..24]</c>: committing to the RC instead of exposing 192 of its bits.
/// Every word is at most 9 characters, which is what keeps a ≥10-character password from ever
/// appearing as a substring of the kit the register response carries (AC-05).
/// </summary>
public static class Mnemonic
{
    /// <summary>Words of the mnemonic — the kit shape in contracts/http-api.md §3.</summary>
    public const int WordCount = 24;

    // 256 words, one per byte of the digest: no word is longer than 9 characters and none contains
    // the substring "kit", so the register body still spells its single "kit" key exactly once.
    private static readonly string[] Words =
    [
        "able", "about", "above", "acorn", "actor", "acute", "adapt", "admit",
        "adobe", "adopt", "adult", "after", "again", "agent", "agree", "ahead",
        "bail", "baker", "balance", "balloon", "banana", "banner", "barge", "basil",
        "basin", "basket", "batch", "beach", "beacon", "beard", "beast", "become",
        "cabin", "cable", "camel", "canal", "candle", "canoe", "canvas", "canyon",
        "carbon", "cargo", "carve", "castle", "casual", "cattle", "celery", "cement",
        "dairy", "dance", "danger", "deal", "debate", "debris", "decade", "decent",
        "decide", "deck", "deep", "deer", "delay", "delta", "dense", "depot",
        "eager", "eagle", "early", "earth", "east", "easy", "echo", "edge",
        "eight", "either", "elbow", "elder", "elect", "elite", "else", "ember",
        "fabric", "factor", "fading", "falcon", "family", "famine", "famous", "fencer",
        "fern", "ferry", "fever", "fiber", "field", "fierce", "figure", "filter",
        "galaxy", "gallery", "game", "gamma", "garden", "garlic", "gauge", "gavel",
        "gecko", "gender", "genre", "gentle", "ghetto", "giant", "ginger", "given",
        "habit", "handy", "harbor", "hardy", "harm", "harvest", "haste", "hatch",
        "haven", "heading", "health", "hearth", "heavy", "hedge", "helmet", "hidden",
        "ice", "icon", "idea", "idle", "ignite", "image", "index", "indigo",
        "inner", "input", "intro", "iris", "iron", "island", "ivory", "ivy",
        "jacket", "jaguar", "jail", "jam", "jasmine", "jaw", "jelly", "jersey",
        "jet", "jewel", "jolly", "journey", "judge", "juice", "july", "jungle",
        "kayak", "keen", "kernel", "kettle", "keyboard", "khaki", "kidney", "kind",
        "kingdom", "kiosk", "knack", "knee", "knit", "knight", "knot", "koala",
        "label", "labor", "ladder", "lagoon", "lamp", "lance", "lantern", "laptop",
        "lark", "laser", "latch", "laundry", "lava", "lawn", "layer", "lazy",
        "magic", "magnet", "mango", "manner", "mantle", "marble", "margin", "marine",
        "market", "marsh", "mask", "master", "matter", "meadow", "medal", "melody",
        "nature", "navy", "near", "neck", "needle", "nerve", "never", "nickel",
        "night", "noble", "noise", "noodle", "normal", "north", "notch", "novel",
        "oasis", "ocean", "offer", "olive", "omega", "onion", "onyx", "opal",
        "open", "opera", "orbit", "orchard", "order", "organ", "origin", "otter",
        "paddle", "palace", "panda", "panel", "panic", "paper", "parade", "parcel",
        "pasta", "pastry", "patch", "path", "patio", "pattern", "pause", "peace",
    ];

    /// <summary>The 24 kit words of <paramref name="recoveryCode"/>, in digest order.</summary>
    public static string[] FromRecoveryCode(byte[] recoveryCode)
    {
        ArgumentNullException.ThrowIfNull(recoveryCode);

        var digest = SHA256.HashData(recoveryCode);
        var words = new string[WordCount];
        for (var i = 0; i < WordCount; i++)
        {
            words[i] = Words[digest[i]];
        }

        return words;
    }
}
