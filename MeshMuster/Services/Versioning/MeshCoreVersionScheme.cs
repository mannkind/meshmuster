using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MeshMuster.Data.Entities;

namespace MeshMuster.Services.Versioning;

/// <summary>
/// Reads the MeshCore firmware tags, where the prefix names the role.
/// </summary>
public partial class MeshCoreVersionScheme : IVersionScheme
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

        foreach (var rejected in Rejected)
            if (tag.StartsWith(rejected, StringComparison.OrdinalIgnoreCase))
                return false;

        foreach (var (prefix, streams) in Prefixes)
        {
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var remainder = tag[prefix.Length..];
            var match = LeadingVersion().Match(remainder);
            if (!match.Success) return false;

            var resolved = streams?.ToList() ?? StreamsFromAssets(assetNames);
            if (resolved.Count == 0) return false;

            var version = match.Groups[1].Value;
            var detail = remainder[match.Length..].Trim('-').Replace('-', ' ');

            parsed = new ParsedRelease
            {
                StreamSlugs = resolved,
                VersionLabel = version,
                SortKey = VersionSortKey.FromDottedNumber(version),
                Detail = detail,
            };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Longest prefix first; "repeater-v" would otherwise eat "repeater-room-v".
    /// </summary>
    private static readonly (string Prefix, string[]? Streams)[] Prefixes =
    [
        ("repeater-room-v", [RepeaterStream, RoomStream]),
        ("room-server-v",   [RoomStream]),
        ("companion-v",     [CompanionStream]),
        ("repeater-v",      [RepeaterStream]),
        ("utility-v",       [SensorStream]),
        ("kiss-v",          [KissStream]),
        ("v",               null),   // null: ask the assets
    ];

    /// <summary>
    /// Tags that look versioned but aren't firmware.
    /// </summary>
    private static readonly string[] Rejected = ["lora-ota-v", "room-v"];

    /// <summary>
    /// The streams a bare "v" tag feeds, read off its asset names.
    /// </summary>
    /// <param name="assetNames"></param>
    private static List<string> StreamsFromAssets(IReadOnlyList<string> assetNames)
    {
        var roles = assetNames.Select(AssetRole.Of).Where(role => role is not null).ToHashSet();
        var streams = DeviceRole.All.Where(roles.Contains).ToList();

        // An assetless release is still a companion release; that's what the early tags were.
        if (streams.Count == 0 && assetNames.Count == 0)
            streams.Add(CompanionStream);

        return streams;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)*)")]
    private static partial Regex LeadingVersion();
}
