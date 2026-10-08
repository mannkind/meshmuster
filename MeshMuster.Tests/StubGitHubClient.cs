using MeshMuster.Services.GitHub;

namespace MeshMuster.Tests;

/// <summary>
/// A queue of canned GitHub responses, one per call.
/// </summary>
public class StubGitHubClient : IGitHubReleaseClient
{
    public int CallCount { get; private set; }

    public List<string> SeenEtags { get; } = [];

    /// <summary>
    /// A release with the shape the schemes expect.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="tag"></param>
    /// <param name="prerelease"></param>
    /// <param name="assetNames"></param>
    public static GitHubRelease Release(
        long id, string tag, bool prerelease = false, params string[] assetNames) =>
        new()
        {
            Id = id,
            TagName = tag,
            Name = tag,
            PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(id),
            Prerelease = prerelease,
            HtmlUrl = $"https://github.test/{tag}",
            Assets = assetNames.Select(n => new GitHubAsset
            {
                Name = n,
                BrowserDownloadUrl = $"https://github.test/{tag}/{n}",
                Size = 1024,
            }).ToList(),
        };

    /// <summary>
    /// Queue a page of releases.
    /// </summary>
    /// <param name="releases"></param>
    public StubGitHubClient Returns(params GitHubRelease[] releases)
    {
        this.Responses.Enqueue(() => new GitHubReleasePage
        {
            Releases = releases,
            Etag = "etag-1",
        });
        return this;
    }

    /// <summary>
    /// Queue a page of releases under a given etag.
    /// </summary>
    /// <param name="etag"></param>
    /// <param name="releases"></param>
    public StubGitHubClient ReturnsWithEtag(string etag, params GitHubRelease[] releases)
    {
        this.Responses.Enqueue(() => new GitHubReleasePage
        {
            Releases = releases,
            Etag = etag,
        });
        return this;
    }

    /// <summary>
    /// Queue a 304.
    /// </summary>
    /// <param name="etag"></param>
    public StubGitHubClient ReturnsNotModified(string etag = "etag-1")
    {
        this.Responses.Enqueue(() => new GitHubReleasePage
        {
            Etag = etag,
            NotModified = true,
        });
        return this;
    }

    /// <summary>
    /// Queue a failure.
    /// </summary>
    /// <param name="message"></param>
    public StubGitHubClient Throws(string message = "boom")
    {
        this.Responses.Enqueue(() => throw new HttpRequestException(message));
        return this;
    }

    /// <inheritdoc />
    public Task<GitHubReleasePage> GetReleasesAsync(
        string owner, string repo, string etag, CancellationToken ct = default)
    {
        this.CallCount++;
        this.SeenEtags.Add(etag);

        // Nothing queued reads as "nothing changed", so an extra poll is harmless.
        if (this.Responses.Count == 0)
        {
            return Task.FromResult(new GitHubReleasePage
            {
                Etag = etag,
                NotModified = true,
            });
        }

        return Task.FromResult(this.Responses.Dequeue()());
    }

    /// <summary>
    /// The queued responses, one per call.
    /// </summary>
    private readonly Queue<Func<GitHubReleasePage>> Responses = new();
}
