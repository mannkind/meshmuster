using System.Text.RegularExpressions;
using MeshMuster.Data.Entities;

namespace MeshMuster.Services;

/// <summary>
/// Picks a board's assets out of a release by filename pattern.
/// </summary>
public static class AssetMatcher
{
    /// <summary>
    /// The assets matching the pattern, by name; nothing when it is blank or bad.
    /// </summary>
    /// <param name="assets"></param>
    /// <param name="pattern"></param>
    public static IReadOnlyList<ReleaseAsset> Match(
        IEnumerable<ReleaseAsset> assets, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return [];

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return [];
        }

        var matched = new List<ReleaseAsset>();
        foreach (var asset in assets)
        {
            try
            {
                if (regex.IsMatch(asset.Name)) matched.Add(asset);
            }
            catch (RegexMatchTimeoutException)
            {
                // Patterns are hand-written; keep what matched rather than failing the page.
                return matched;
            }
        }

        return matched.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Whether a pattern compiles; a blank one does not count.
    /// </summary>
    /// <param name="pattern"></param>
    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The cap on a single match, so a catastrophic pattern can't hang a request.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
}
