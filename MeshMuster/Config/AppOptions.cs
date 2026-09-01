using System.Net;
using MeshMuster.Services;

namespace MeshMuster.Config;

/// <summary>
/// The options across the application, read from the environment.
/// </summary>
public record AppOptions
{
    public string ListenAddr { get; set; } = ":8080";
    public string StatePath { get; set; } = "./state";
    public string DatabasePath { get; set; } = Path.Combine("./state", "meshmuster.db");
    public string GitHubToken { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public Uri GitHubApiBaseUrl { get; set; } = new("https://api.github.com/");
    public Uri FlasherConfigUrl { get; set; } = new("https://flasher.meshcore.io/config.json");
    public int PollIntervalHours { get; set; } = 6;
    public bool PollOnStartup { get; set; } = true;
    public IReadOnlyList<IPAddress> TrustedProxies { get; set; } = [];
    public IReadOnlyList<IPNetwork> TrustedNetworks { get; set; } = [];

    public string RawGitHubApiBaseUrl { get; set; } = string.Empty;
    public string RawFlasherConfigUrl { get; set; } = string.Empty;
    public string RawTrustedProxies { get; set; } = string.Empty;
    public string RawTrustedNetworks { get; set; } = string.Empty;

    /// <summary>The decoded SECRET_KEY.</summary>
    public byte[]? SecretKeyBytes() => SecretProtector.DecodeKey(this.SecretKey);

    /// <summary>
    /// Every reason the options are unusable; empty when they are fine.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (this.PollIntervalHours is < 1 or > 168)
            errors.Add("POLL_INTERVAL_HOURS must be between 1 and 168.");

        if (this.RawGitHubApiBaseUrl.Length > 0 &&
            (!Uri.TryCreate(this.RawGitHubApiBaseUrl, UriKind.Absolute, out var api) ||
             (api.Scheme != Uri.UriSchemeHttp && api.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("GITHUB_API_BASE_URL must be an absolute HTTP(S) URL.");
        }

        if (this.RawFlasherConfigUrl.Length > 0 &&
            (!Uri.TryCreate(this.RawFlasherConfigUrl, UriKind.Absolute, out var flasher) ||
             (flasher.Scheme != Uri.UriSchemeHttp && flasher.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("FLASHER_CONFIG_URL must be an absolute HTTP(S) URL.");
        }

        foreach (var raw in Split(this.RawTrustedProxies))
            if (!IPAddress.TryParse(raw, out _))
                errors.Add($"TRUSTED_PROXIES contains an invalid IP address: '{raw}'.");

        foreach (var raw in Split(this.RawTrustedNetworks))
            if (!IPNetwork.TryParse(raw, out _))
                errors.Add($"TRUSTED_NETWORKS contains an invalid CIDR network: '{raw}'.");

        if (string.IsNullOrWhiteSpace(this.StatePath))
            errors.Add("STATE_PATH must not be empty.");

        if (string.IsNullOrWhiteSpace(this.DatabasePath))
            errors.Add("DATABASE_PATH must not be empty.");

        if (this.SecretKeyBytes() is null)
        {
            errors.Add(
                $"SECRET_KEY must be {SecretProtector.KeyBytes} bytes of base64; " +
                "generate one with `openssl rand -base64 32`.");
        }

        return errors;
    }

    /// <summary>
    /// The URL to listen on; a bare ":port" means every interface.
    /// </summary>
    public string GetListenUrl()
    {
        var addr = this.ListenAddr.Trim();
        return addr.StartsWith(':') ? $"http://0.0.0.0{addr}" : $"http://{addr}";
    }

    /// <summary>
    /// Split a comma separated setting, dropping blanks.
    /// </summary>
    /// <param name="value"></param>
    internal static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
