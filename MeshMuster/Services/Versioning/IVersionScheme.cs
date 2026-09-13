using System.Diagnostics.CodeAnalysis;

namespace MeshMuster.Services.Versioning;

/// <summary>
/// Turns one repo's tag naming into streams and a sortable version.
/// </summary>
public interface IVersionScheme
{
    /// <summary>
    /// Parse a tag; false for anything this scheme doesn't recognise.
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="assetNames"></param>
    /// <param name="parsed"></param>
    bool TryParse(
        string tag,
        IReadOnlyList<string> assetNames,
        [NotNullWhen(true)] out ParsedRelease? parsed);
}
