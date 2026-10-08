using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace MeshMuster.E2E;

/// <summary>
/// A stand-in for the GitHub releases API, so the E2E run never leaves the machine.
/// </summary>
public class FakeGitHubServer : IDisposable
{
    public string BaseUrl => this.Server.Url + "/";

    public FakeGitHubServer()
    {
        this.Server
            .Given(Request.Create().WithPath("/flasher-config.json").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                staticPath = "/firmware",
                device = new[]
                {
                    new
                    {
                        firmware = new[]
                        {
                            new
                            {
                                role = "kissRadio",
                                version = new Dictionary<string, object>
                                {
                                    ["1.17.1"] = new
                                    {
                                        files = new[]
                                        {
                                            new { name = "Xiao_nrf52_kiss_modem-1.17.1.uf2" },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            }));
    }

    /// <summary>
    /// Serve a repo's releases.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="repo"></param>
    /// <param name="releases"></param>
    public void SetReleases(
        string owner, string repo, params (long Id, string Tag, bool Pre, string[] Assets)[] releases)
    {
        var body = releases.Select(object (r) => new
        {
            id = r.Id,
            tag_name = r.Tag,
            name = r.Tag,
            prerelease = r.Pre,
            draft = false,
            published_at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(r.Id),
            html_url = $"https://github.test/{r.Tag}",
            assets = r.Assets.Select(object (a) => new
            {
                name = a,
                browser_download_url = $"https://github.test/{r.Tag}/{a}",
                size = 1024,
            }).ToList(),
        }).ToList();

        var path = $"/repos/{owner}/{repo}/releases";

        this.Server
            .Given(Request.Create().WithPath(path).WithParam("page", "1").UsingGet())
            .RespondWith(Response.Create()
                .WithHeader("ETag", $"\"{body.Count}\"")
                .WithBodyAsJson(body));

        // Page two onward is empty, which is how the client learns to stop.
        this.Server
            .Given(Request.Create().WithPath(path).UsingGet())
            .AtPriority(10)
            .RespondWith(Response.Create()
                .WithHeader("ETag", $"\"{body.Count}\"")
                .WithBodyAsJson(Array.Empty<object>()));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Server.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The stub server, on a port of its choosing.
    /// </summary>
    private readonly WireMockServer Server = WireMockServer.Start();
}
