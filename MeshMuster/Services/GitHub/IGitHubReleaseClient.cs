namespace MeshMuster.Services.GitHub;

/// <summary>
/// A managed way to read releases from GitHub.
/// </summary>
public interface IGitHubReleaseClient
{
    /// <summary>
    /// Every release in a repo; pass the stored etag to get a 304 when nothing changed.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="etag"></param>
    /// <param name="ct"></param>
    Task<GitHubReleasePage> GetReleasesAsync(
        string owner, string repo, string etag, CancellationToken ct = default);
}
