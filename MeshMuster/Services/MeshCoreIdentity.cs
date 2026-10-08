using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;

namespace MeshMuster.Services;

/// <summary>
/// Derives a MeshCore public key from a private key.
/// </summary>
/// <remarks>
/// Ed25519 scalar multiplication by hand; the firmware stores the expanded 64-byte key and
/// uses the first 32 as the scalar, unclamped, so nothing in the BCL does this for us.
/// </remarks>
public static class MeshCoreIdentity
{
    public const int PrivateKeyBytes = 64;
    public const int PublicKeyBytes = 32;
    public const int PrefixBytes = 3;

    /// <summary>
    /// The public key as hex, or null when the private key is unusable.
    /// </summary>
    /// <param name="privateKeyHex"></param>
    public static string? DerivePublicKey(string? privateKeyHex)
    {
        var normalized = Normalize(privateKeyHex);
        if (normalized is null) return null;

        if (Cache.TryGetValue(normalized, out var cached)) return cached;

        if (!TryParseHex(normalized, out var privateKey)) return null;

        // Little-endian, the way the firmware reads it.
        var scalar = new BigInteger(privateKey.AsSpan(0, 32), isUnsigned: true, isBigEndian: false);
        var publicKey = Encode(Multiply(BasePoint, scalar));

        if (Cache.Count < MaxCached) Cache.TryAdd(normalized, publicKey);
        return publicKey;
    }

    /// <summary>
    /// The leading bytes of the public key, which is how a node is named on the air.
    /// </summary>
    /// <param name="privateKeyHex"></param>
    /// <param name="bytes"></param>
    public static string? DerivePublicKeyPrefix(string? privateKeyHex, int bytes = PrefixBytes)
    {
        var publicKey = DerivePublicKey(privateKeyHex);
        if (publicKey is null) return null;

        var chars = Math.Clamp(bytes, 1, PublicKeyBytes) * 2;
        return publicKey[..chars];
    }

    /// <summary>
    /// The cap on cached keys; a device list derives the same handful over and over.
    /// </summary>
    private const int MaxCached = 512;

    /// <summary>
    /// Private key to derived public key.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// The curve prime, 2^255 - 19.
    /// </summary>
    private static readonly BigInteger Q = BigInteger.Pow(2, 255) - 19;

    /// <summary>
    /// The curve constant d.
    /// </summary>
    private static readonly BigInteger D = Mod(-121665 * Inverse(121666));

    // Extended coordinates, the same shape ge_p3 uses.
    private static readonly BigInteger[] BasePoint = MakeBasePoint();

    /// <summary>
    /// Upper-cased key of the right length, or null.
    /// </summary>
    /// <param name="hex"></param>
    private static string? Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;

        var trimmed = hex.Trim();
        return trimmed.Length == PrivateKeyBytes * 2 ? trimmed.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Parse the key; false on the first byte that isn't hex.
    /// </summary>
    /// <param name="hex"></param>
    /// <param name="bytes"></param>
    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        bytes = new byte[PrivateKeyBytes];
        for (var i = 0; i < PrivateKeyBytes; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out bytes[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The Ed25519 base point, y = 4/5.
    /// </summary>
    private static BigInteger[] MakeBasePoint()
    {
        var y = Mod(4 * Inverse(5));
        var x = RecoverX(y);
        return [x, y, BigInteger.One, Mod(x * y)];
    }

    /// <summary>
    /// The even x for a y on the curve.
    /// </summary>
    /// <param name="y"></param>
    private static BigInteger RecoverX(BigInteger y)
    {
        var xx = Mod((y * y - 1) * Inverse(Mod(D * y * y + 1)));
        var x = BigInteger.ModPow(xx, (Q + 3) / 8, Q);

        // The root came out of the wrong square; multiply by sqrt(-1).
        if (Mod(x * x - xx) != 0)
            x = Mod(x * BigInteger.ModPow(2, (Q - 1) / 4, Q));

        return x.IsEven ? x : Q - x;
    }

    /// <summary>
    /// Add two points in extended coordinates.
    /// </summary>
    /// <param name="p"></param>
    /// <param name="q"></param>
    private static BigInteger[] Add(BigInteger[] p, BigInteger[] q)
    {
        var a = Mod((p[1] - p[0]) * (q[1] - q[0]));
        var b = Mod((p[1] + p[0]) * (q[1] + q[0]));
        var c = Mod(2 * p[3] * q[3] * D);
        var dd = Mod(2 * p[2] * q[2]);

        var e = b - a;
        var f = dd - c;
        var g = dd + c;
        var h = b + a;

        return [Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h)];
    }

    /// <summary>
    /// Double-and-add; not constant time, and it doesn't need to be for a public key.
    /// </summary>
    /// <param name="point"></param>
    /// <param name="scalar"></param>
    private static BigInteger[] Multiply(BigInteger[] point, BigInteger scalar)
    {
        BigInteger[] result = [BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero];
        var addend = point;

        while (scalar > 0)
        {
            if (!scalar.IsEven) result = Add(result, addend);
            addend = Add(addend, addend);
            scalar >>= 1;
        }

        return result;
    }

    /// <summary>
    /// Compress a point to 32 bytes of hex; the top bit carries x's sign.
    /// </summary>
    /// <param name="point"></param>
    private static string Encode(BigInteger[] point)
    {
        var zi = Inverse(point[2]);
        var x = Mod(point[0] * zi);
        var y = Mod(point[1] * zi);

        var bytes = new byte[PublicKeyBytes];
        y.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
        if (!x.IsEven) bytes[31] |= 0x80;

        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Modular inverse by Fermat; Q is prime.
    /// </summary>
    /// <param name="value"></param>
    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), Q - 2, Q);

    /// <summary>
    /// Reduce mod Q, never negative.
    /// </summary>
    /// <param name="value"></param>
    private static BigInteger Mod(BigInteger value)
    {
        var result = value % Q;
        return result.Sign < 0 ? result + Q : result;
    }
}
