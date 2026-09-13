using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeshMuster.Config;
using Microsoft.Extensions.Options;

namespace MeshMuster.Services.GitHub;

/// <summary>
/// The official flasher lists KISS firmware that is absent from the MeshCore GitHub releases.
/// </summary>
public sealed class FlasherCatalogClient
{
    public FlasherCatalogClient(HttpClient http, IOptions<AppOptions> options)
    {
        this.Http = http;
        this.Options = options;
    }

    public async Task<IReadOnlyList<GitHubRelease>> GetKissReleasesAsync(CancellationToken ct)
    {
        var configUrl = this.Options.Value.FlasherConfigUrl;
        await using var stream = await this.Http.GetStreamAsync(configUrl, ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var staticPath = root.GetProperty("staticPath").GetString() ?? "/firmware";
        var versions = new Dictionary<string, Dictionary<string, GitHubAsset>>(StringComparer.Ordinal);

        foreach (var device in root.GetProperty("device").EnumerateArray())
        {
            if (!device.TryGetProperty("firmware", out var firmwares)) continue;
            foreach (var firmware in firmwares.EnumerateArray())
            {
                if (!firmware.TryGetProperty("role", out var role) ||
                    role.GetString() != "kissRadio" ||
                    !firmware.TryGetProperty("version", out var releases)) continue;

                foreach (var version in releases.EnumerateObject())
                {
                    if (!version.Value.TryGetProperty("files", out var files)) continue;
                    if (!versions.TryGetValue(version.Name, out var assets))
                        versions[version.Name] = assets = new Dictionary<string, GitHubAsset>(StringComparer.Ordinal);

                    foreach (var file in files.EnumerateArray())
                    {
                        if (!file.TryGetProperty("name", out var nameProperty)) continue;
                        var name = nameProperty.GetString();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        assets[name] = new GitHubAsset
                        {
                            Name = name,
                            BrowserDownloadUrl = new Uri(configUrl,
                                staticPath.TrimEnd('/') + "/" + Uri.EscapeDataString(name)).ToString(),
                        };
                    }
                }
            }
        }

        return versions.Select(version => new GitHubRelease
        {
            Id = SyntheticId(version.Key),
            TagName = "kiss-v" + version.Key,
            Name = "Official KISS firmware " + version.Key,
            PublishedAt = DateTimeOffset.UnixEpoch,
            HtmlUrl = new Uri(configUrl, "/").ToString(),
            Assets = version.Value.Values.ToList(),
        }).ToList();
    }

    private static long SyntheticId(string version)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("official-kiss:" + version));
        return -1 - (BinaryPrimitives.ReadInt64BigEndian(hash) & long.MaxValue);
    }

    private readonly HttpClient Http;
    private readonly IOptions<AppOptions> Options;
}
