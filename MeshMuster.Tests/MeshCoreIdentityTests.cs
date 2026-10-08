using MeshMuster.Services;

namespace MeshMuster.Tests;

/// <summary>
/// Deriving a public key, checked against keys a real node reported.
/// </summary>
public class MeshCoreIdentityTests
{
    [TestCase(
        "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279" +
        "896EC7ED326E0237BAB3A7B12BE04D07E33676B81670C4ABB822E6713BA8F3C8",
        "ABC1225608250503E7B73C49AA384635FCDDE1CD455922FFD46D486CA27D5923")]
    [TestCase(
        "D0C3BA7281628723C5C8F48F939830A6B1F534E3C2764E9DE3828E1824E56770" +
        "CF53535013BB557F94E6ABFFB1FEFE7438932BB25185D9C8FB21407C7BC823C2",
        "123AB1FD3D59BDA2292806F13B7B27A9F97D3E0863F884BFFB7F785FA84369FF")]
    [TestCase(
        "80E2C120E99456EE58799E51266D26334ABEAE19733E9EF42647EC6ED07CF242" +
        "7FD23AF38474050193083BC3BE4D9FA39FEBFDEEFC2A62882DF3476CF23DF076",
        "BADB06BE7238C36563232B101A868654C0241B67B68362211AAEDDAFF591F013")]
    public void Derives_the_public_key_from_a_real_private_key(string privateKey, string publicKey)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MeshCoreIdentity.DerivePublicKey(privateKey), Is.EqualTo(publicKey));
            Assert.That(MeshCoreIdentity.DerivePublicKeyPrefix(privateKey),
                Is.EqualTo(publicKey[..6]));
            Assert.That(privateKey, Does.Not.Contain(publicKey[..16]).IgnoreCase);
        });
    }

    [Test]
    public void The_second_half_of_the_private_key_is_not_the_public_key() =>
        Assert.That(PrivateKey[64..], Is.Not.EqualTo(PublicKey).IgnoreCase);

    [Test]
    public void Derives_the_three_byte_prefix() =>
        Assert.That(MeshCoreIdentity.DerivePublicKeyPrefix(PrivateKey), Is.EqualTo("ABC122"));

    [Test]
    public void Prefix_length_is_configurable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MeshCoreIdentity.DerivePublicKeyPrefix(PrivateKey, 1), Is.EqualTo("AB"));
            Assert.That(MeshCoreIdentity.DerivePublicKeyPrefix(PrivateKey, 2), Is.EqualTo("ABC1"));
            Assert.That(MeshCoreIdentity.DerivePublicKeyPrefix(PrivateKey, 32), Is.EqualTo(PublicKey));
        });
    }

    [Test]
    public void Accepts_lowercase_and_padded_input() =>
        Assert.That(MeshCoreIdentity.DerivePublicKey($"  {PrivateKey.ToLowerInvariant()}  "),
            Is.EqualTo(PublicKey));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("?")]
    [TestCase("NOTHEX")]
    [TestCase("70B4941F")]                       // too short
    public void Returns_null_for_anything_that_is_not_a_64_byte_key(string? value)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MeshCoreIdentity.DerivePublicKey(value), Is.Null);
            Assert.That(MeshCoreIdentity.DerivePublicKeyPrefix(value), Is.Null);
        });
    }

    [Test]
    public void Rejects_a_64_byte_string_that_is_not_hex()
    {
        var notHex = new string('Z', 128);

        Assert.That(MeshCoreIdentity.DerivePublicKey(notHex), Is.Null);
    }

    [Test]
    public void Repeated_derivation_is_stable()
    {
        var first = MeshCoreIdentity.DerivePublicKey(PrivateKey);
        var second = MeshCoreIdentity.DerivePublicKey(PrivateKey);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Deriving_many_keys_stays_fast_enough_to_render_a_list()
    {
        var start = DateTime.UtcNow;
        for (var i = 0; i < 20; i++)
        {
            // Vary the key so the cache can't answer.
            var key = PrivateKey[..126] + i.ToString("X2");
            _ = MeshCoreIdentity.DerivePublicKey(key);
        }

        Assert.That(DateTime.UtcNow - start, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    private const string PrivateKey =
        "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279" +
        "896EC7ED326E0237BAB3A7B12BE04D07E33676B81670C4ABB822E6713BA8F3C8";

    private const string PublicKey =
        "ABC1225608250503E7B73C49AA384635FCDDE1CD455922FFD46D486CA27D5923";
}
