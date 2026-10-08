namespace MeshMuster.Data.Entities;

/// <summary>
/// One upstream release, as it lands in a single stream.
/// </summary>
public class Release
{
    public Guid Id { get; set; }
    public Guid StreamId { get; set; }
    public ReleaseStream? Stream { get; set; }

    public long GithubId { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>The version as shown to a person.</summary>
    public string VersionLabel { get; set; } = string.Empty;

    /// <summary>The zero-padded key releases sort by; ordinal compares work on it.</summary>
    public string SortKey { get; set; } = string.Empty;

    /// <summary>Whatever trailed the version in the tag.</summary>
    public string Detail { get; set; } = string.Empty;

    public bool IsPrerelease { get; set; }
    public DateTime PublishedAt { get; set; }
    public string HtmlUrl { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; }

    public ICollection<ReleaseAsset> Assets { get; set; } = [];
}
