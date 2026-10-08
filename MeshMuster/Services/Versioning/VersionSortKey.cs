using System.Text.RegularExpressions;

namespace MeshMuster.Services.Versioning;

/// <summary>
/// Builds the zero-padded key that makes versions compare ordinally, in SQL and in memory.
/// </summary>
public static class VersionSortKey
{
    public const int Components = 4;
    public const string Unknown = "";

    /// <summary>
    /// A sort key from the numbers in a version; Unknown when it has none.
    /// </summary>
    /// <param name="dotted"></param>
    public static string FromDottedNumber(string dotted)
    {
        var parts = NumericRun.Matches(dotted)
            .Select(m => m.Value)
            .Take(Components)
            .ToList();

        if (parts.Count == 0) return Unknown;

        // Pad out, so 1.2 sorts below 1.2.1 rather than beside it.
        while (parts.Count < Components) parts.Add("0");

        return string.Join('.', parts.Select(Pad));
    }

    /// <summary>
    /// The numbers within a version string.
    /// </summary>
    private static readonly Regex NumericRun = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Five digits wide; anything longer keeps its tail.
    /// </summary>
    /// <param name="number"></param>
    private static string Pad(string number)
    {
        var trimmed = number.TrimStart('0');
        if (trimmed.Length == 0) trimmed = "0";
        if (trimmed.Length > 5) trimmed = trimmed[^5..];
        return trimmed.PadLeft(5, '0');
    }
}
