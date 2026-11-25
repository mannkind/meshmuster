using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MeshMuster.Data.Entities;

namespace MeshMuster.Services.Versioning;

/// <summary>
/// Reads the MeshCore firmware tags, where the prefix names the role.
/// </summary>
public partial class MeshCoreVersionScheme(bool mikecarper = false) : IVersionScheme
{
    // The stream slugs are the role names, in both repos.
    public const string CompanionStream = DeviceRole.Companion;
    public const string RepeaterStream = DeviceRole.Repeater;
    public const string RoomStream = DeviceRole.Room;
    public const string SensorStream = DeviceRole.Sensor;
    public const string KissStream = DeviceRole.Kiss;

    /// <inheritdoc />
    public bool TryParse(
        string tag, IReadOnlyList<string> assetNames, [NotNullWhen(true)] out ParsedRelease? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;

        foreach (var (prefix, streams) in Prefixes)
        {
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var remainder = tag[prefix.Length..];
            var match = LeadingVersion().Match(remainder);
            if (!match.Success) return false;

            var version = match.Groups[1].Value;
            var sortKey = VersionSortKey.FromDottedNumber(version);
            bool bundle = streams is null && prefix != "v";
            bool splitLayout = mikecarper && string.CompareOrdinal(sortKey, "00001.00018.00000.00000") >= 0;
            if ((bundle && !splitLayout) || (prefix == "utility-v" && splitLayout)) return false;

            var resolved = streams ?? StreamsFromAssets(assetNames, prefix == "v");
            if (resolved.Count == 0) return false;

            var detail = remainder[match.Length..].Trim('-').Replace('-', ' ');
            if (bundle) detail = $"{prefix[..^2]} {detail}".TrimEnd();

            parsed = new ParsedRelease
            {
                StreamSlugs = resolved,
                VersionLabel = version,
                SortKey = sortKey,
                Detail = detail,
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Longest prefix first; "repeater-v" would otherwise eat "repeater-room-v".
    /// </summary>
    private static readonly (string Prefix, IReadOnlyList<string>? Streams)[] Prefixes =
    [
        ("repeater-room-v", [RepeaterStream, RoomStream]),
        ("room-server-v",   [RoomStream]),
        ("companion-v",     [CompanionStream]),
        ("repeater-v",      [RepeaterStream]),
        ("utility-v",       [SensorStream]),
        ("kiss-v",          [KissStream]),
        ("full-profiles-v", null),
        ("lora-ota-v",      null),
        ("lora-ota-2-v",    null),
        ("lora-ota-3-v",    null),
        ("v",               null),   // null: ask the assets
    ];

    private static List<string> StreamsFromAssets(IReadOnlyList<string> assetNames, bool bareTag)
    {
        var roles = assetNames.Select(AssetRole.Of).Where(role => role is not null).ToHashSet();
        var streams = DeviceRole.All.Where(roles.Contains).ToList();

        // An assetless release is still a companion release; that's what the early tags were.
        if (bareTag && assetNames.Count == 0)
            streams.Add(CompanionStream);

        return streams;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)*)")]
    private static partial Regex LeadingVersion();
}
