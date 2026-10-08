using System.Security.Cryptography;
using MeshMuster.Services;

namespace MeshMuster.Tests;

/// <summary>
/// What goes into the column, and what refuses to come back out.
/// </summary>
public class SecretProtectorTests
{
    [Test]
    public void Round_trips_a_private_key()
    {
        var protector = new SecretProtector(Key(1));
        const string key = "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279";

        Assert.That(protector.Unprotect(protector.Protect(key)), Is.EqualTo(key));
    }

    [Test]
    public void The_ciphertext_does_not_contain_the_plaintext()
    {
        var protector = new SecretProtector(Key(1));
        var stored = protector.Protect("SECRET-KEY-VALUE");

        Assert.Multiple(() =>
        {
            Assert.That(stored, Does.Not.Contain("SECRET-KEY-VALUE"));
            Assert.That(stored, Does.StartWith(SecretProtector.Prefix));
        });
    }

    [Test]
    public void The_same_value_encrypts_differently_every_time()
    {
        var protector = new SecretProtector(Key(1));
        var first = protector.Protect("abc");
        var second = protector.Protect("abc");

        // Fresh nonce per write; two nodes sharing a password would look alike otherwise.
        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void Blank_stays_blank()
    {
        var protector = new SecretProtector(Key(1));

        Assert.Multiple(() =>
        {
            Assert.That(protector.Protect(string.Empty), Is.Empty);
            Assert.That(protector.Unprotect(string.Empty), Is.Empty);
        });
    }

    [Test]
    public void Another_key_will_not_decrypt_it()
    {
        var stored = new SecretProtector(Key(1)).Protect("abc");

        Assert.That(() => new SecretProtector(Key(2)).Unprotect(stored),
            Throws.InstanceOf<CryptographicException>());
    }

    [Test]
    public void A_tampered_ciphertext_will_not_decrypt()
    {
        var protector = new SecretProtector(Key(1));
        var stored = protector.Protect("abc");

        // Flip a base64 character; the tag is there to catch it.
        var flipped = stored[..^2] + (stored[^2] == 'A' ? 'B' : 'A') + stored[^1];

        Assert.That(() => protector.Unprotect(flipped), Throws.InstanceOf<CryptographicException>());
    }

    [Test]
    public void A_plaintext_row_throws_rather_than_reading_as_itself()
    {
        var protector = new SecretProtector(Key(1));

        Assert.That(() => protector.Unprotect("70B4941F5DB88E45"),
            Throws.InstanceOf<CryptographicException>().With.Message.Contains("not encrypted"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not base64 at all")]
    [TestCase("c2hvcnQ=")]
    public void Refuses_a_key_that_is_not_32_bytes_of_base64(string value) =>
        Assert.That(SecretProtector.DecodeKey(value), Is.Null);

    [Test]
    public void Accepts_a_key_that_is_32_bytes_of_base64() =>
        Assert.That(SecretProtector.DecodeKey(Convert.ToBase64String(Key(1))), Has.Length.EqualTo(32));

    [Test]
    public void Refuses_to_be_built_with_the_wrong_key_size() =>
        Assert.That(() => new SecretProtector(new byte[16]), Throws.InstanceOf<ArgumentException>());

    /// <summary>
    /// A distinct 32-byte key per seed.
    /// </summary>
    /// <param name="seed"></param>
    private static byte[] Key(byte seed) =>
        Enumerable.Range(0, SecretProtector.KeyBytes).Select(i => (byte)(i + seed)).ToArray();
}
