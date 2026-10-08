using MeshMuster.Services.Versioning;

namespace MeshMuster.Tests;

/// <summary>
/// Reading the MeshCore firmware tags, against the ones both repos really publish.
/// </summary>
public class MeshCoreVersionSchemeTests
{
    [TestCase("repeater-v1.17.1", "repeater", "1.17.1", "00001.00017.00001.00000")]
    [TestCase("companion-v1.17.1", "companion", "1.17.1", "00001.00017.00001.00000")]
    [TestCase("repeater-v1.16.0", "repeater", "1.16.0", "00001.00016.00000.00000")]
    [TestCase("room-server-v1.17.1", "room", "1.17.1", "00001.00017.00001.00000")]
    [TestCase("v1.17.1.5-halo-keymind-cascade-dev-26303793",
        "companion", "1.17.1.5", "00001.00017.00001.00005")]
    [TestCase("v1.17.1.1-halo-keymind-cascade-759a35fc",
        "companion", "1.17.1.1", "00001.00017.00001.00001")]
    public void Parses_real_tags(string tag, string stream, string label, string sortKey)
    {
        Assert.That(this.Scheme.TryParse(tag, Companion, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.StreamSlugs.Single(), Is.EqualTo(stream));
            Assert.That(parsed.VersionLabel, Is.EqualTo(label));
            Assert.That(parsed.SortKey, Is.EqualTo(sortKey));
        });
    }

    [TestCase("lora-ota-v1.17.1.5-halo-keymind-cascade-dev-26303793")]
    [TestCase("room-v6")]
    [TestCase("")]
    [TestCase("some-branch-name")]
    public void Rejects_tags_that_belong_to_no_tracked_stream(string tag) =>
        Assert.That(this.Scheme.TryParse(tag, Companion, out _), Is.False);

    [Test]
    public void Build_suffix_is_detail_not_ordering()
    {
        this.Scheme.TryParse("v1.17.1.5-halo-keymind-cascade-dev-26303793", Companion, out var a);
        this.Scheme.TryParse("v1.17.1.5-halo-keymind-cascade-dev-11111111", Companion, out var b);

        Assert.That(a!.SortKey, Is.EqualTo(b!.SortKey));
        Assert.That(a.Detail, Is.Not.Empty);
    }

    [Test]
    public void A_bare_mikecarper_tag_carrying_repeater_builds_serves_both_streams()
    {
        Assert.That(this.Mikecarper.TryParse(
            "v1.17.1.1-halo-keymind-cascade-759a35fc", Omnibus, out var parsed), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.StreamSlugs, Does.Contain("companion"));
            Assert.That(parsed.StreamSlugs, Does.Contain("repeater"));
            Assert.That(parsed.StreamSlugs, Does.Contain("room"));
            Assert.That(parsed.VersionLabel, Is.EqualTo("1.17.1.1"));
        });
    }

    [Test]
    public void A_bare_mikecarper_tag_with_only_companion_builds_stays_companion_only()
    {
        Assert.That(this.Mikecarper.TryParse(
            "v1.17.1.5-halo-keymind-cascade-dev-26303793", Companion, out var parsed), Is.True);

        Assert.That(parsed!.StreamSlugs, Is.EqualTo(new[] { "companion" }));
    }

    [Test]
    public void An_explicit_repeater_room_tag_feeds_both_streams_without_asset_inspection()
    {
        Assert.That(this.Mikecarper.TryParse(
            "repeater-room-v1.17.1.5-halo-keymind-cascade-dev-26303793", [], out var parsed), Is.True);

        Assert.That(parsed!.StreamSlugs, Is.EqualTo(new[] { "repeater", "room" }));
    }

    [TestCase("utility-v1.17.1.5-halo-keymind-cascade-dev-26303793", "sensor", "1.17.1.5")]
    [TestCase("kiss-v1.17.1.1-halo-keymind-cascade-759a35fc", "kiss", "1.17.1.1")]
    public void Parses_the_mikecarper_sensor_and_kiss_tags(string tag, string stream, string label)
    {
        Assert.That(this.Mikecarper.TryParse(tag, Omnibus, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.StreamSlugs, Is.EqualTo(new[] { stream }));
            Assert.That(parsed.VersionLabel, Is.EqualTo(label));
        });
    }

    [Test]
    public void A_bare_tag_carrying_sensor_builds_reaches_the_sensor_stream()
    {
        string[] withSensor = [.. Omnibus, "RAK_3401_sensor-v1.17.1.1-759a35fc.uf2"];

        Assert.That(this.Mikecarper.TryParse(
            "v1.17.1.1-halo-keymind-cascade-759a35fc", withSensor, out var parsed), Is.True);

        Assert.That(parsed!.StreamSlugs,
            Is.EqualTo(new[] { "repeater", "companion", "room", "sensor" }));
    }

    [TestCase("logging-v1.16.09-halo-keymind-cascade-dev-0c4ed3ac")]
    [TestCase("full-profiles-v1.17.1.1-halo-keymind-cascade-759a35fc")]
    [TestCase("nrf52-mota-v1.16.09-to-v1.17.1.1-halo-keymind-cascade")]
    [TestCase("tbeam1w-lna-v1.17.1.1-d28c9f74")]
    public void Rejects_mikecarper_variant_builds(string tag) =>
        Assert.That(this.Mikecarper.TryParse(tag, Omnibus, out _), Is.False);

    [Test]
    public void Sort_keys_order_versions_as_strings()
    {
        string Key(string tag) { this.Scheme.TryParse(tag, Repeater, out var p); return p!.SortKey; }

        var ordered = new[]
        {
            Key("repeater-v1.9.0"),
            Key("repeater-v1.16.0"),
            Key("repeater-v1.17.1"),
            Key("repeater-v1.17.1.5-x"),
        };

        Assert.That(ordered, Is.Ordered.Using<string>(StringComparer.Ordinal));
    }

    private readonly MeshCoreVersionScheme Scheme = new();
    private readonly MeshCoreVersionScheme Mikecarper = new();

    private static readonly string[] Companion = ["Xiao_companion-v1.uf2"];
    private static readonly string[] Repeater = ["Heltec_t096_repeater-v1.uf2"];

    // What the older bare tags shipped: every role's firmware in one release.
    private static readonly string[] Omnibus =
    [
        "Xiao_companion-v1.17.1.1-759a35fc.uf2",
        "Heltec_t096_repeater-v1.17.1.1-759a35fc.uf2",
        "Heltec_t096_room_server-v1.17.1.1-759a35fc.uf2",
    ];
}

/// <summary>
/// Reading the OTAFIX bootloader tags.
/// </summary>
public class OtafixVersionSchemeTests
{
    [TestCase("0.9.2-OTAFIX2.3-BP1.4", "OTAFIX 2.3", "00002.00003.00000.00000")]
    [TestCase("0.9.2-OTAFIX2.2-BP1.3", "OTAFIX 2.2", "00002.00002.00000.00000")]
    [TestCase("0.9.2-OTAFIX2.1-BP1.2", "OTAFIX 2.1", "00002.00001.00000.00000")]
    [TestCase("0.9.2-OTAFIX1.2-BP1.2", "OTAFIX 1.2", "00001.00002.00000.00000")]
    [TestCase("0.11.0-OTAFIX2.4.6", "OTAFIX 2.4.6", "00002.00004.00006.00000")]
    [TestCase("0.11.0-OTAFIX2.4.2", "OTAFIX 2.4.2", "00002.00004.00002.00000")]
    public void Parses_real_tags(string tag, string label, string sortKey)
    {
        Assert.That(this.Scheme.TryParse(tag, NoAssets, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.StreamSlugs.Single(), Is.EqualTo("default"));
            Assert.That(parsed.VersionLabel, Is.EqualTo(label));
            Assert.That(parsed.SortKey, Is.EqualTo(sortKey));
        });
    }

    [Test]
    public void Carries_adafruit_base_and_board_package_as_detail()
    {
        this.Scheme.TryParse("0.9.2-OTAFIX2.3-BP1.4", NoAssets, out var withBoardPackage);
        this.Scheme.TryParse("0.11.0-OTAFIX2.4.6", NoAssets, out var withoutBoardPackage);

        Assert.Multiple(() =>
        {
            Assert.That(withBoardPackage!.Detail, Is.EqualTo("Adafruit 0.9.2 - BP 1.4"));
            Assert.That(withoutBoardPackage!.Detail, Is.EqualTo("Adafruit 0.11.0"));
        });
    }

    [Test]
    public void Adafruit_base_version_does_not_affect_ordering()
    {
        this.Scheme.TryParse("0.9.2-OTAFIX2.4.6", NoAssets, out var oldBase);
        this.Scheme.TryParse("0.11.0-OTAFIX2.4.6", NoAssets, out var newBase);

        Assert.That(oldBase!.SortKey, Is.EqualTo(newBase!.SortKey));
    }

    [TestCase("0.9.2")]
    [TestCase("0.10.0")]
    [TestCase("0.11.0")]
    [TestCase("softdevice-uf2")]
    [TestCase("push")]
    [TestCase("")]
    public void Rejects_upstream_adafruit_and_branch_tags(string tag) =>
        Assert.That(this.Scheme.TryParse(tag, NoAssets, out _), Is.False);

    [Test]
    public void Mikecarper_otafix_sorts_above_oltaco_otafix()
    {
        this.Scheme.TryParse("0.9.2-OTAFIX2.2-BP1.3", NoAssets, out var oltaco);
        this.Scheme.TryParse("0.11.0-OTAFIX2.4.6", NoAssets, out var mikecarper);

        Assert.That(string.CompareOrdinal(mikecarper!.SortKey, oltaco!.SortKey), Is.GreaterThan(0));
    }

    private readonly OtafixVersionScheme Scheme = new();
    private static readonly string[] NoAssets = [];
}

/// <summary>
/// The padded sort key, which every version comparison leans on.
/// </summary>
public class VersionSortKeyTests
{
    [Test]
    public void Unknown_sorts_before_every_real_version()
    {
        var real = VersionSortKey.FromDottedNumber("0.0.0.1");

        Assert.That(string.CompareOrdinal(VersionSortKey.Unknown, real), Is.LessThan(0));
    }

    [Test]
    public void Pads_to_four_components()
    {
        Assert.That(VersionSortKey.FromDottedNumber("1.17"),
            Is.EqualTo("00001.00017.00000.00000"));
    }

    [Test]
    public void Ignores_components_past_the_fourth()
    {
        Assert.That(VersionSortKey.FromDottedNumber("1.2.3.4.5.6"),
            Is.EqualTo("00001.00002.00003.00004"));
    }

    [Test]
    public void Numeric_ordering_beats_lexical_ordering()
    {
        var nine = VersionSortKey.FromDottedNumber("1.9.0");
        var seventeen = VersionSortKey.FromDottedNumber("1.17.0");

        Assert.Multiple(() =>
        {
            Assert.That(string.CompareOrdinal(nine, seventeen), Is.LessThan(0));
            Assert.That(string.CompareOrdinal("1.9.0", "1.17.0"), Is.GreaterThan(0));
        });
    }

    [Test]
    public void Text_without_digits_is_unknown() =>
        Assert.That(VersionSortKey.FromDottedNumber("nightly"), Is.EqualTo(VersionSortKey.Unknown));
}
