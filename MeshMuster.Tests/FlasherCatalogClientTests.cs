using System.Net;
using MeshMuster.Config;
using MeshMuster.Services.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshMuster.Tests;

public class FlasherCatalogClientTests
{
    [Test]
    public async Task Kiss_releases_are_available_even_when_github_returns_not_modified()
    {
        var options = Options.Create(new AppOptions
        {
            GitHubApiBaseUrl = new Uri("https://github.test/"),
            FlasherConfigUrl = new Uri("https://flasher.test/config.json"),
        });
        using var githubHttp = new HttpClient(new Handler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotModified)));
        using var flasherHttp = new HttpClient(new Handler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "staticPath": "/firmware",
                      "device": [
                        {"firmware": [{"role": "kissRadio", "version": {
                          "1.17.1": {"files": [{"name": "Xiao_nrf52_kiss_modem.uf2"}]}
                        }}]},
                        {"firmware": [{"role": "kissRadio", "version": {
                          "1.17.1": {"files": [{"name": "Xiao_nrf52_kiss_modem.uf2"}]}
                        }}]}
                      ]
                    }
                    """),
            }));

        var github = new GitHubReleaseClient(githubHttp, options,
            NullLogger<GitHubReleaseClient>.Instance);
        var flasher = new FlasherCatalogClient(flasherHttp, options);
        var page = await new CompositeReleaseClient(github, flasher)
            .GetReleasesAsync("meshcore-dev", "MeshCore", "\"old-etag\"");

        Assert.Multiple(() =>
        {
            Assert.That(page.NotModified, Is.False);
            Assert.That(page.Releases, Has.Count.EqualTo(1));
            Assert.That(page.Releases[0].TagName, Is.EqualTo("kiss-v1.17.1"));
            Assert.That(page.Releases[0].Assets, Has.Count.EqualTo(1));
            Assert.That(page.Releases[0].Assets[0].BrowserDownloadUrl,
                Is.EqualTo("https://flasher.test/firmware/Xiao_nrf52_kiss_modem.uf2"));
        });
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
