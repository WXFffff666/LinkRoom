namespace LinkRoom.Core.Tests;

/// <summary>
/// Validates the pure-C# X25519 implementation in SecureModeKeys against the
/// RFC 7748 §6.1 test vectors. The keypair is what easytier-core uses for its
/// Noise static keys, so a wrong ladder would silently break every secure-mode
/// connection (or worse, mis-verify pins).
/// </summary>
public class SecureModeKeysTests
{
    static byte[] Hex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    [Fact]
    public void Rfc7748_AlicePrivateKey_DerivesAlicePublicKey()
    {
        // RFC 7748 §6.1: X25519(a, 9) — the exact operation DerivePublicKey
        // performs (base point u = 9).
        var scalar = Hex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
        var expected = "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a";

        var actual = SecureModeKeys.DerivePublicKey(scalar);

        Assert.Equal(expected, Convert.ToHexString(actual).ToLowerInvariant());
    }

    [Fact]
    public void Rfc7748_BobPrivateKey_DerivesBobPublicKey()
    {
        var scalar = Hex("5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb");
        var expected = "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f";

        var actual = SecureModeKeys.DerivePublicKey(scalar);

        Assert.Equal(expected, Convert.ToHexString(actual).ToLowerInvariant());
    }

    [Fact]
    public void DerivePublicKey_RejectsNon32ByteScalar()
    {
        Assert.Throws<ArgumentException>(() => SecureModeKeys.DerivePublicKey(new byte[31]));
        Assert.Throws<ArgumentException>(() => SecureModeKeys.DerivePublicKey(new byte[33]));
    }

    [Fact]
    public void ClampScalar_AppliesRfc7748Clamping()
    {
        // RFC 7748 §5: bits 0-2 clear, bit 255 clear, bit 254 set.
        var scalar = new byte[32];
        var clamped = SecureModeKeys.ClampScalar(scalar);

        Assert.Equal(0, (int)(clamped & 7));            // bits 0..2 clear
        Assert.Equal(0, (int)((clamped >> 255) & 1));   // bit 255 clear
        Assert.Equal(1, (int)((clamped >> 254) & 1));   // bit 254 set
    }
}
