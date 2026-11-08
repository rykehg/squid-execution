using System.Buffers.Binary;
using Squid.Api.Auth;

namespace Squid.Tests.Auth;

/// <summary>B-12/FR-005: the kit carries 24 words that the recovery code reproduces, and only words
/// that keep the register response safe to search for "kit".</summary>
public sealed class MnemonicTests
{
    [Fact]
    public void The_mnemonic_is_24_words_determined_by_the_recovery_code()
    {
        var recoveryCode = new byte[32];
        BinaryPrimitives.WriteInt64LittleEndian(recoveryCode, 1);

        var first = Mnemonic.FromRecoveryCode(recoveryCode);
        var second = Mnemonic.FromRecoveryCode(recoveryCode);
        Assert.Equal(Mnemonic.WordCount, first.Length);
        Assert.Equal(first, second); // /api/recovery/kit must reproduce exactly these words

        BinaryPrimitives.WriteInt64LittleEndian(recoveryCode, 2);
        Assert.NotEqual(first, Mnemonic.FromRecoveryCode(recoveryCode));
    }

    [Fact]
    public void Every_word_reachable_from_a_key_is_short_and_never_spells_kit()
    {
        // Walking the digest space until all 256 words show up also proves the list has no
        // duplicate: a duplicate would leave the count short of 256 no matter how long we walk.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 4096 && seen.Count < 256; attempt++)
        {
            var recoveryCode = new byte[32];
            BinaryPrimitives.WriteInt64LittleEndian(recoveryCode, attempt);
            foreach (var word in Mnemonic.FromRecoveryCode(recoveryCode))
            {
                seen.Add(word);
            }
        }

        Assert.Equal(256, seen.Count);
        foreach (var word in seen)
        {
            // A password is at least 10 characters, so none of these can ever be a fragment of one
            // inside the register body; and no word may spell "kit" beside the response's own key.
            Assert.InRange(word.Length, 3, 9);
            Assert.DoesNotContain("kit", word, StringComparison.OrdinalIgnoreCase);
        }
    }
}
