using System.Text.Json.Serialization;

namespace MeshMuster.Services.GitHub;

/// <summary>
/// A release, as the GitHub API returns it.
/// </summary>
public record GitHubRelease
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("tag_name")] public string TagName { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
    [JsonPropertyName("draft")] public bool Draft { get; init; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; init; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; init; } = string.Empty;
    [JsonPropertyName("assets")] public IReadOnlyList<GitHubAsset> Assets { get; init; } = [];
}

/// <summary>
/// An asset, as the GitHub API returns it.
/// </summary>
public record GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; init; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; init; }
}

/// <summary>
/// Every release a poll fetched, and the etag to replay next time.
/// </summary>
public record GitHubReleasePage
{
    public IReadOnlyList<GitHubRelease> Releases { get; init; } = [];

    public string Etag { get; init; } = string.Empty;

    /// <summary>True when GitHub answered 304 and Releases is therefore empty.</summary>
    public bool NotModified { get; init; }
}
