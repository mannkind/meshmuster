using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MeshMuster.Config;
using Microsoft.Extensions.Options;

namespace MeshMuster.Services.GitHub;

/// <summary>
/// A managed way to read releases from GitHub.
/// </summary>
public class GitHubReleaseClient : IGitHubReleaseClient
{
    /// <summary>
    /// Initializes a new instance of the GitHubReleaseClient class.
    /// </summary>
    /// <param name="http"></param>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    public GitHubReleaseClient(
        HttpClient http,
        IOptions<AppOptions> options,
        ILogger<GitHubReleaseClient> logger)
    {
        this.Http = http;
        this.Options = options;
        this.Logger = logger;
    }

    /// <summary>
    /// Set up the headers and timeout GitHub wants.
    /// </summary>
    /// <param name="client"></param>
    /// <param name="options"></param>
    public static void Configure(HttpClient client, AppOptions options)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MeshMuster/1.0");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(options.GitHubToken))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.GitHubToken);
    }

    /// <inheritdoc />
    public async Task<GitHubReleasePage> GetReleasesAsync(
        string owner, string repo, string etag, CancellationToken ct = default)
    {
        var baseUrl = this.Options.Value.GitHubApiBaseUrl;
        var all = new List<GitHubRelease>();
        var firstPageEtag = string.Empty;

        var perPage = PerPage;
        var page = 1;

        while (page <= MaxPages)
        {
            var (response, usedPerPage) = await this.FetchPageAsync(
                baseUrl, owner, repo, page, perPage, page == 1 ? etag : string.Empty, ct).ConfigureAwait(false);

            using (response)
            {
                // Only the first page carries an etag, so a 304 there means the whole repo.
                if (page == 1 && response.StatusCode == HttpStatusCode.NotModified)
                {
                    this.Logger.LogDebug("{Owner}/{Repo} unchanged since last poll", owner, repo);
                    return new GitHubReleasePage { Etag = etag, NotModified = true };
                }

                response.EnsureSuccessStatusCode();

                if (page == 1)
                    firstPageEtag = response.Headers.ETag?.ToString() ?? string.Empty;

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(body, JsonOpts) ?? [];

                all.AddRange(releases);

                // Short page means last page; asking again only burns rate limit.
                if (releases.Count < usedPerPage) break;
            }

            perPage = usedPerPage;
            page++;
        }

        return new GitHubReleasePage { Releases = all, Etag = firstPageEtag };
    }

    /// <summary>
    /// The serializer options used internally.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Releases per page.
    /// </summary>
    private const int PerPage = 25;

    /// <summary>
    /// For when even that is too much.
    /// </summary>
    private const int SmallPerPage = 10;

    /// <summary>
    /// The cap on paging, so a runaway repo can't loop forever.
    /// </summary>
    private const int MaxPages = 60;

    /// <summary>
    /// The statuses GitHub returns when a page is too big to assemble.
    /// </summary>
    private static readonly HttpStatusCode[] GatewayFailures =
        [HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout];

    /// <summary>
    /// The client used to access the source.
    /// </summary>
    private readonly HttpClient Http;

    /// <summary>
    /// The options used internally.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<GitHubReleaseClient> Logger;

    /// <summary>
    /// Fetch one page, retrying smaller when GitHub times out assembling it.
    /// </summary>
    /// <param name="baseUrl"></param>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="page"></param>
    /// <param name="perPage"></param>
    /// <param name="etag"></param>
    /// <param name="ct"></param>
    private async Task<(HttpResponseMessage Response, int PerPage)> FetchPageAsync(
        Uri baseUrl, string owner, string repo, int page, int perPage, string etag, CancellationToken ct)
    {
        var response = await this.SendAsync(baseUrl, owner, repo, page, perPage, etag, ct).ConfigureAwait(false);

        if (!GatewayFailures.Contains(response.StatusCode) || perPage <= SmallPerPage)
            return (response, perPage);

        this.Logger.LogWarning(
            "{Owner}/{Repo} returned {Status} at {PerPage} per page; retrying at {Small}",
            owner, repo, (int)response.StatusCode, perPage, SmallPerPage);

        response.Dispose();
        var retried = await this.SendAsync(baseUrl, owner, repo, page, SmallPerPage, etag, ct).ConfigureAwait(false);
        return (retried, SmallPerPage);
    }

    /// <summary>
    /// Send one releases request.
    /// </summary>
    /// <param name="baseUrl"></param>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="page"></param>
    /// <param name="perPage"></param>
    /// <param name="etag"></param>
    /// <param name="ct"></param>
    private Task<HttpResponseMessage> SendAsync(
        Uri baseUrl, string owner, string repo, int page, int perPage, string etag, CancellationToken ct)
    {
        var url = new Uri(baseUrl, $"repos/{owner}/{repo}/releases?per_page={perPage}&page={page}");
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        return this.Http.SendAsync(request, ct);
    }
}
