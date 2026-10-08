using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace MeshMuster.Services.Versioning;

/// <summary>
/// Reads the OTAFIX bootloader tags, which carry the Adafruit base and board package too.
/// </summary>
public partial class OtafixVersionScheme : IVersionScheme
{
    public const string StreamSlug = "default";

    /// <inheritdoc />
    public bool TryParse(
        string tag, IReadOnlyList<string> assetNames, [NotNullWhen(true)] out ParsedRelease? parsed)
    {
        // One stream per bootloader repo, so the assets tell us nothing new.
        _ = assetNames;

        parsed = null;
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var otafix = OtafixVersion().Match(tag);
        if (!otafix.Success) return false;

        var version = otafix.Groups[1].Value;

        var details = new List<string>();
        var adafruit = AdafruitBase().Match(tag);
        if (adafruit.Success) details.Add($"Adafruit {adafruit.Groups[1].Value}");
        var boardPackage = BoardPackage().Match(tag);
        if (boardPackage.Success) details.Add($"BP {boardPackage.Groups[1].Value}");

        parsed = new ParsedRelease
        {
            StreamSlugs = [StreamSlug],
            VersionLabel = $"OTAFIX {version}",
            SortKey = VersionSortKey.FromDottedNumber(version),
            Detail = string.Join(" - ", details),
        };
        return true;
    }

    [GeneratedRegex(@"OTAFIX[\s-]*(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex OtafixVersion();

    [GeneratedRegex(@"^(\d+(?:\.\d+)+)-")]
    private static partial Regex AdafruitBase();

    [GeneratedRegex(@"BP(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase)]
    private static partial Regex BoardPackage();
}
