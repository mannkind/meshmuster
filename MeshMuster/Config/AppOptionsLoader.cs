using System.Net;

namespace MeshMuster.Config;

/// <summary>
/// Reads AppOptions out of configuration.
/// </summary>
public static class AppOptionsLoader
{
    /// <summary>
    /// A fresh AppOptions filled from configuration.
    /// </summary>
    /// <param name="config"></param>
    public static AppOptions Load(IConfiguration config)
    {
        var options = new AppOptions();
        Populate(options, config);
        return options;
    }

    /// <summary>
    /// Fill an existing AppOptions from configuration.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="config"></param>
    public static void Populate(AppOptions options, IConfiguration config)
    {
        options.ListenAddr = Value(config, "LISTEN_ADDR") ?? ":8080";
        options.StatePath = Value(config, "STATE_PATH") ?? "./state";
        options.DatabasePath =
            Value(config, "DATABASE_PATH") ?? Path.Combine(options.StatePath, "meshmuster.db");
        options.GitHubToken = Value(config, "GITHUB_TOKEN") ?? string.Empty;
        options.SecretKey = Value(config, "SECRET_KEY") ?? string.Empty;
        options.PollOnStartup = config.GetValue("POLL_ON_STARTUP", true);
        options.PollIntervalHours = config.GetValue("POLL_INTERVAL_HOURS", 6);

        // Anything unparsable falls back here and is reported by AppOptions.Validate.
        options.RawGitHubApiBaseUrl = Value(config, "GITHUB_API_BASE_URL") ?? string.Empty;
        options.GitHubApiBaseUrl =
            Uri.TryCreate(EnsureTrailingSlash(options.RawGitHubApiBaseUrl), UriKind.Absolute, out var api)
                ? api
                : new Uri("https://api.github.com/");

        options.RawFlasherConfigUrl = Value(config, "FLASHER_CONFIG_URL") ?? string.Empty;
        options.FlasherConfigUrl =
            Uri.TryCreate(options.RawFlasherConfigUrl, UriKind.Absolute, out var flasher)
                ? flasher
                : new Uri("https://flasher.meshcore.io/config.json");

        options.RawTrustedProxies = Value(config, "TRUSTED_PROXIES") ?? string.Empty;
        options.TrustedProxies = AppOptions.Split(options.RawTrustedProxies)
            .Select(v => IPAddress.TryParse(v, out var ip) ? ip : null)
            .OfType<IPAddress>()
            .ToList();

        options.RawTrustedNetworks = Value(config, "TRUSTED_NETWORKS") ?? string.Empty;
        options.TrustedNetworks = AppOptions.Split(options.RawTrustedNetworks)
            .Select(v => IPNetwork.TryParse(v, out var net) ? net : (IPNetwork?)null)
            .OfType<IPNetwork>()
            .ToList();
    }

    /// <summary>
    /// A base URL needs the slash, or Uri drops the last path segment.
    /// </summary>
    /// <param name="value"></param>
    private static string EnsureTrailingSlash(string value) =>
        value.Length == 0 || value.EndsWith('/') ? value : value + "/";

    /// <summary>
    /// A trimmed setting, or null when it is blank.
    /// </summary>
    /// <param name="config"></param>
    /// <param name="key"></param>
    private static string? Value(IConfiguration config, string key)
    {
        var raw = config[key];
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
