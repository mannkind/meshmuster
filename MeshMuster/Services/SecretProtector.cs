using System.Security.Cryptography;
using System.Text;

namespace MeshMuster.Services;

/// <summary>
/// AES-256-GCM over the private key and admin password columns.
/// </summary>
/// <remarks>
/// The key is in SECRET_KEY, not the state volume; protects a backup, not a live host.
/// </remarks>
public sealed class SecretProtector
{
    /// <summary>What SECRET_KEY has to decode to.</summary>
    public const int KeyBytes = 32;

    /// <summary>Stamped on every ciphertext; an unprefixed row predates encryption.</summary>
    public const string Prefix = "v1.";

    /// <summary>
    /// Initializes a new instance of the SecretProtector class.
    /// </summary>
    /// <param name="key"></param>
    public SecretProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyBytes)
            throw new ArgumentException($"The key must be exactly {KeyBytes} bytes.", nameof(key));

        this.Key = (byte[])key.Clone();
    }

    /// <summary>The decoded SECRET_KEY, or null when it's unusable.</summary>
    /// <param name="value"></param>
    public static byte[]? DecodeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        Span<byte> buffer = stackalloc byte[KeyBytes + 1];
        if (!Convert.TryFromBase64String(value.Trim(), buffer, out var written)) return null;

        return written == KeyBytes ? buffer[..KeyBytes].ToArray() : null;
    }

    /// <summary>The value as it goes to the database; blank stays blank.</summary>
    /// <param name="value"></param>
    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(this.Key, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // nonce || tag || ciphertext, so the fixed-width parts come off the front.
        var packed = new byte[NonceBytes + TagBytes + ciphertext.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, NonceBytes);
        ciphertext.CopyTo(packed, NonceBytes + TagBytes);

        return Prefix + Convert.ToBase64String(packed);
    }

    /// <summary>
    /// The value as the app reads it; a failure throws rather than reading as blank.
    /// </summary>
    /// <param name="stored"></param>
    public string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "A stored secret is not encrypted; it was written before SECRET_KEY existed.");
        }

        byte[] packed;
        try
        {
            packed = Convert.FromBase64String(stored[Prefix.Length..]);
        }
        catch (FormatException e)
        {
            throw new CryptographicException("A stored secret is not valid base64.", e);
        }

        if (packed.Length < NonceBytes + TagBytes)
            throw new CryptographicException("A stored secret is too short to be ciphertext.");

        var plaintext = new byte[packed.Length - NonceBytes - TagBytes];

        using var aes = new AesGcm(this.Key, TagBytes);
        try
        {
            aes.Decrypt(
                packed.AsSpan(0, NonceBytes),
                packed.AsSpan(NonceBytes + TagBytes),
                packed.AsSpan(NonceBytes, TagBytes),
                plaintext);
        }
        catch (CryptographicException e)
        {
            // Wrong key, or someone edited the column by hand.
            throw new CryptographicException(
                "A stored secret would not decrypt under the current SECRET_KEY.", e);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>GCM's nonce, fresh per write.</summary>
    private const int NonceBytes = 12;

    /// <summary>GCM's authentication tag.</summary>
    private const int TagBytes = 16;

    /// <summary>The key, copied so the caller can't clear it out from under us.</summary>
    private readonly byte[] Key;
}
