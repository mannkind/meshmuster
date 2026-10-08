namespace MeshMuster.Services.Versioning;

/// <summary>
/// What a version scheme made of one upstream tag.
/// </summary>
public record ParsedRelease
{
    /// <summary>The streams this release belongs in; a tag can feed more than one.</summary>
    public IReadOnlyList<string> StreamSlugs { get; init; } = [];

    /// <summary>The version as shown to a person.</summary>
    public string VersionLabel { get; init; } = string.Empty;

    /// <summary>The zero-padded key releases sort by.</summary>
    public string SortKey { get; init; } = string.Empty;

    /// <summary>Whatever trailed the version in the tag.</summary>
    public string Detail { get; init; } = string.Empty;
}
