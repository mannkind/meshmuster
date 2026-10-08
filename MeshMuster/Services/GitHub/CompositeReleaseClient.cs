namespace MeshMuster.Services.GitHub;

/// <summary>
/// Adds the official flasher's KISS releases to the official GitHub release feed.
/// </summary>
public sealed class CompositeReleaseClient : IGitHubReleaseClient
{
    public CompositeReleaseClient(GitHubReleaseClient gitHub, FlasherCatalogClient flasher)
    {
        this.GitHub = gitHub;
        this.Flasher = flasher;
    }

    public async Task<GitHubReleasePage> GetReleasesAsync(
        string owner, string repo, string etag, CancellationToken ct = default)
    {
        var page = await this.GitHub.GetReleasesAsync(owner, repo, etag, ct);
        if (owner != "meshcore-dev" || repo != "MeshCore") return page;

        var kiss = await this.Flasher.GetKissReleasesAsync(ct);
        if (kiss.Count == 0) return page;

        // GitHub may answer 304 even when the separate flasher catalog has changed.
        return new GitHubReleasePage
        {
            Etag = page.Etag,
            Releases = [.. page.Releases, .. kiss],
        };
    }

    private readonly GitHubReleaseClient GitHub;
    private readonly FlasherCatalogClient Flasher;
}
