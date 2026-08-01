using System.Numerics;
using System.Security.Cryptography;

namespace LinkRoom.Core;

/// <summary>
/// EasyTier secure-mode keypair management.
///
/// easytier-core 2.6.4 does NOT auto-generate secure-mode keys when loading
/// a config file (only the CLI --secure-mode flag path does — see
/// SPIKE-SECUREMODE.md), so LinkRoom must supply both
/// <c>[secure_mode] local_private_key</c> and <c>local_public_key</c>. The
/// private key is generated once and persisted at
/// <c>LinkRoomData/config/securemode.key</c>; the public key is derived with
/// an X25519 scalar multiplication (no BCL X25519 in .NET 10, no NuGet
/// allowed) and validated against RFC 7748 test vectors in the test suite.
///
/// A stable per-install keypair also matches the official recommendation for
/// shared nodes: "--local-private-key 建议显式固定，避免重启后公钥变化".
/// </summary>
public static class SecureModeKeys
{
    const string KeyFileName = "securemode.key";

    // RFC 7748: field prime 2^255 - 19.
    static readonly BigInteger P = (BigInteger.One << 255) - 19;
    // a24 = (486662 - 2) / 4 = 121665, used in the Montgomery ladder step.
    const long A24 = 121665;
    static readonly BigInteger BasePointU = 9;

    static readonly object Gate = new();
    static (string Private, string Public)? _cached;

    public static string KeyPath => Path.Combine(AppPaths.ConfigDir, KeyFileName);

    /// <summary>
    /// Returns the persisted (or freshly generated) keypair as base64 X25519
    /// keys. Never throws for I/O failures — secure mode simply degrades to a
    /// fresh random keypair for the current run (same as easytier's CLI path).
    /// </summary>
    public static (string Private, string Public) LoadOrCreate()
    {
        lock (Gate)
        {
            if (_cached != null) return _cached.Value;

            var privateKey = TryReadKeyFile();
            if (privateKey == null)
            {
                privateKey = RandomNumberGenerator.GetBytes(32);
                TryWriteKeyFile(privateKey);
            }

            _cached = (Convert.ToBase64String(privateKey), Convert.ToBase64String(DerivePublicKey(privateKey)));
            return _cached.Value;
        }
    }

    /// <summary>For tests: reset the in-memory cache (the key file itself is left untouched).</summary>
    public static void ResetCache() => _cached = null;

    static byte[]? TryReadKeyFile()
    {
        try
        {
            if (!File.Exists(KeyPath)) return null;
            var bytes = Convert.FromBase64String(File.ReadAllText(KeyPath).Trim());
            return bytes.Length == 32 ? bytes : null;
        }
        catch { return null; }
    }

    static void TryWriteKeyFile(byte[] privateKey)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigDir);
            File.WriteAllText(KeyPath, Convert.ToBase64String(privateKey));
        }
        catch { /* ephemeral keypair for this run */ }
    }

    /// <summary>
    /// X25519 public key: u-coordinate of clamped(private) * base point (u = 9).
    /// Montgomery ladder over GF(2^255-19), little-endian per RFC 7748.
    /// </summary>
    public static byte[] DerivePublicKey(byte[] privateKey)
    {
        if (privateKey.Length != 32)
            throw new ArgumentException("X25519 private key must be 32 bytes.", nameof(privateKey));

        var u = X25519Ladder(ClampScalar(privateKey), BasePointU);

        var result = new byte[32];
        for (int i = 0; i < 32; i++)
            result[i] = (byte)((u >> (8 * i)) & 0xff);
        return result;
    }

    /// <summary>RFC 7748 §5: clamp the 32-byte little-endian scalar.</summary>
    public static BigInteger ClampScalar(byte[] privateKey)
    {
        var k = (byte[])privateKey.Clone();
        k[0] &= 248;
        k[31] &= 127;
        k[31] |= 64;
        return new BigInteger(k, isUnsigned: true, isBigEndian: false);
    }

    static BigInteger X25519Ladder(BigInteger k, BigInteger x1)
    {
        var u = x1 % P;
        BigInteger x2 = 1, z2 = 0, x3 = u, z3 = 1;
        var swap = 0;

        for (int t = 254; t >= 0; t--)
        {
            var kt = (int)((k >> t) & 1);
            swap ^= kt;
            if (swap == 1)
            {
                (x2, x3) = (x3, x2);
                (z2, z3) = (z3, z2);
            }
            swap = kt;

            var a = (x2 + z2) % P;
            var aa = a * a % P;
            var b = ModSub(x2, z2);
            var bb = b * b % P;
            var e = ModSub(aa, bb);
            var c = (x3 + z3) % P;
            var d = ModSub(x3, z3);
            var da = d * a % P;
            var cb = c * b % P;
            x3 = (da + cb) * (da + cb) % P;
            z3 = u * ModSub(da, cb) % P * ModSub(da, cb) % P;
            x2 = aa * bb % P;
            z2 = e * ((aa + A24 * e) % P) % P;
        }

        if (swap == 1)
        {
            (x2, x3) = (x3, x2);
            (z2, z3) = (z3, z2);
        }

        return x2 * BigInteger.ModPow(z2, P - 2, P) % P;
    }

    static BigInteger ModSub(BigInteger a, BigInteger b)
    {
        var r = (a - b) % P;
        return r < 0 ? r + P : r;
    }
}
