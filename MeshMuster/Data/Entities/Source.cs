namespace MeshMuster.Data.Entities;

/// <summary>
/// What a source publishes.
/// </summary>
public static class SourceKind
{
    public const string Firmware = "firmware";
    public const string Bootloader = "bootloader";
}

/// <summary>
/// A GitHub repository releases are polled from.
/// </summary>
public class Source
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Kind { get; set; } = SourceKind.Firmware;
    public string DisplayName { get; set; } = string.Empty;
    public string GithubOwner { get; set; } = string.Empty;
    public string GithubRepo { get; set; } = string.Empty;
    public bool IncludePrereleases { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Replayed as If-None-Match, so an unchanged repo costs no rate limit.</summary>
    public string Etag { get; set; } = string.Empty;

    public DateTime? LastPolledAt { get; set; }
    public string LastPollError { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ReleaseStream> Streams { get; set; } = [];
    public ICollection<BoardAssetPattern> BoardAssetPatterns { get; set; } = [];

    public string RepoSlug => $"{this.GithubOwner}/{this.GithubRepo}";
    public string HtmlUrl => $"https://github.com/{this.GithubOwner}/{this.GithubRepo}";
}
