using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Squid.Api.Auth;

/// <summary>
/// Argon2id of the login password (SPEC-0007 B-04). The gap adjudication is explicit:
/// <c>Squid.Api/Crypto</c> seals the `RK`, it does not hash passwords, so the password helper lives
/// beside the auth surface and uses the same BouncyCastle package the csproj already carries.
/// The stored form is the PHC string <c>$argon2id$v=19$m=4096,t=3,p=1$salt$hash</c>, which keeps
/// the cost parameters with the hash — a future parameter bump still verifies what is on disk.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Minimum password length of B-04; the 422 message cites this number.</summary>
    public const int MinLength = 10;

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Algorithm = "argon2id";
    private const string Prefix = "$argon2id$v=19$";

    // BouncyCastle's own Argon2id defaults, registered as constants exactly like RootKeyCipher does,
    // so a package bump cannot silently change what a login costs (SPEC-0007 non-functional).
    private const int Argon2Iterations = 3;
    private const int Argon2MemoryKib = 4096;
    private const int Argon2Parallelism = 1;

    /// <summary>
    /// A hash nothing is ever stored with: a login for a handle the instance does not know verifies
    /// against it, so "unknown handle" and "wrong password" cost the same Argon2id run and neither
    /// enumerates the account (B-05, AC-03).
    /// </summary>
    public static string DummyHash { get; } = Hash("squid-timing-equalizer");

    /// <summary>New salted Argon2id hash; the same password never produces the same string twice.</summary>
    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, Argon2MemoryKib, Argon2Iterations, Argon2Parallelism);
        return Prefix
            + $"m={Argon2MemoryKib},t={Argon2Iterations},p={Argon2Parallelism}"
            + "$" + Convert.ToBase64String(salt)
            + "$" + Convert.ToBase64String(hash);
    }

    /// <summary>
    /// True only when <paramref name="password"/> produces the hash in <paramref name="encoded"/>.
    /// A malformed or foreign hash is "false", never an exception: this runs on a trust boundary and
    /// a bad row must fail the login instead of the request.
    /// </summary>
    public static bool Verify(string password, string encoded)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrEmpty(encoded)) return false;

        // $argon2id$v=19$m=4096,t=3,p=1$<salt>$<hash>
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[1] != Algorithm) return false;

        var parameters = parts[3].Split(',');
        if (parameters.Length != 3) return false;
        if (!TryParameter(parameters[0], "m=", out var memoryKib)) return false;
        if (!TryParameter(parameters[1], "t=", out var iterations)) return false;
        if (!TryParameter(parameters[2], "p=", out var parallelism)) return false;
        if (memoryKib < 1 || iterations < 1 || parallelism < 1) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[4]);
            expected = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0) return false;

        var actual = Derive(password, salt, memoryKib, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryParameter(string field, string name, out int value)
    {
        value = 0;
        return field.StartsWith(name, StringComparison.Ordinal)
            && int.TryParse(field[name.Length..], out value);
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism)
    {
        var generator = new Argon2BytesGenerator();
        generator.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithSalt(salt)
            .WithIterations(iterations)
            .WithMemoryAsKB(memoryKib)
            .WithParallelism(parallelism)
            .Build());

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var hash = new byte[HashSize];
            generator.GenerateBytes(passwordBytes, hash);
            return hash;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
