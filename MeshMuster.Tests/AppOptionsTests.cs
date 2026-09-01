using MeshMuster.Config;
using Microsoft.Extensions.Configuration;

namespace MeshMuster.Tests;

/// <summary>
/// Reading the options out of the environment, and what it refuses.
/// </summary>
public class AppOptionsTests
{

    [Test]
    public void Defaults_are_sane_when_nothing_is_set()
    {
        var opts = Load([]);

        Assert.Multiple(() =>
        {
            Assert.That(opts.ListenAddr, Is.EqualTo(":8080"));
            Assert.That(opts.StatePath, Is.EqualTo("./state"));
            Assert.That(opts.DatabasePath, Is.EqualTo(Path.Combine("./state", "meshmuster.db")));
            Assert.That(opts.GitHubApiBaseUrl, Is.EqualTo(new Uri("https://api.github.com/")));
            Assert.That(opts.FlasherConfigUrl, Is.EqualTo(new Uri("https://flasher.meshcore.io/config.json")));
            Assert.That(opts.PollIntervalHours, Is.EqualTo(6));
            Assert.That(opts.PollOnStartup, Is.True);
            Assert.That(opts.Validate(), Is.Empty);
        });
    }

    [Test]
    public void Database_path_follows_state_path_unless_set_explicitly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Load(new() { ["STATE_PATH"] = "/data" }).DatabasePath,
                Is.EqualTo(Path.Combine("/data", "meshmuster.db")));
            Assert.That(Load(new() { ["STATE_PATH"] = "/data", ["DATABASE_PATH"] = "/x/y.db" }).DatabasePath,
                Is.EqualTo("/x/y.db"));
        });
    }

    [TestCase("0")]
    [TestCase("999")]
    public void Rejects_an_out_of_range_poll_interval(string value) =>
        Assert.That(Load(new() { ["POLL_INTERVAL_HOURS"] = value }).Validate(),
            Has.Some.Contains("POLL_INTERVAL_HOURS"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("hunter2")]
    [TestCase("c2hvcnQ=")]
    public void Refuses_to_start_without_a_usable_secret_key(string? value) =>
        Assert.That(Load(new() { ["SECRET_KEY"] = value }).Validate(),
            Has.Some.Contains("SECRET_KEY"));

    [Test]
    public void Decodes_a_valid_secret_key()
    {
        var opts = Load(new() { ["SECRET_KEY"] = SqliteTestDatabase.TestSecretKey });

        Assert.Multiple(() =>
        {
            Assert.That(opts.SecretKeyBytes(), Has.Length.EqualTo(32));
            Assert.That(opts.Validate(), Is.Empty);
        });
    }

    [Test]
    public void Rejects_a_github_base_url_that_is_not_absolute_http() =>
        Assert.That(Load(new() { ["GITHUB_API_BASE_URL"] = "not-a-url" }).Validate(),
            Has.Some.Contains("GITHUB_API_BASE_URL"));

    [Test]
    public void Rejects_a_flasher_url_that_is_not_absolute_http() =>
        Assert.That(Load(new() { ["FLASHER_CONFIG_URL"] = "not-a-url" }).Validate(),
            Has.Some.Contains("FLASHER_CONFIG_URL"));

    [Test]
    public void Adds_the_trailing_slash_a_relative_uri_join_needs()
    {
        // Without it, joining a relative path quietly eats the last segment.
        var opts = Load(new() { ["GITHUB_API_BASE_URL"] = "http://localhost:9099/api" });

        Assert.That(new Uri(opts.GitHubApiBaseUrl, "repos/a/b/releases").AbsoluteUri,
            Is.EqualTo("http://localhost:9099/api/repos/a/b/releases"));
    }

    [Test]
    public void Rejects_garbage_proxy_addresses() =>
        Assert.That(Load(new() { ["TRUSTED_PROXIES"] = "10.0.0.1,nope" }).Validate(),
            Has.Some.Contains("TRUSTED_PROXIES"));

    [Test]
    public void Rejects_garbage_trusted_networks() =>
        Assert.That(Load(new() { ["TRUSTED_NETWORKS"] = "10.0.0.0/8,nope" }).Validate(),
            Has.Some.Contains("TRUSTED_NETWORKS"));

    [Test]
    public void Parses_valid_proxies_and_networks()
    {
        var opts = Load(new()
        {
            ["TRUSTED_PROXIES"] = "10.0.0.1, 10.0.0.2",
            ["TRUSTED_NETWORKS"] = "192.168.0.0/16",
        });

        Assert.Multiple(() =>
        {
            Assert.That(opts.TrustedProxies, Has.Count.EqualTo(2));
            Assert.That(opts.TrustedNetworks, Has.Count.EqualTo(1));
            Assert.That(opts.Validate(), Is.Empty);
        });
    }

    [TestCase(":9000", "http://0.0.0.0:9000")]
    [TestCase("127.0.0.1:8080", "http://127.0.0.1:8080")]
    public void Expands_the_listen_address(string addr, string expected) =>
        Assert.That(Load(new() { ["LISTEN_ADDR"] = addr }).GetListenUrl(), Is.EqualTo(expected));

    [Test]
    public void Poll_on_startup_defaults_to_true_and_can_be_switched_off()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Load([]).PollOnStartup, Is.True);
            Assert.That(Load(new() { ["POLL_ON_STARTUP"] = "false" }).PollOnStartup, Is.False);
            Assert.That(Load(new() { ["POLL_ON_STARTUP"] = "true" }).PollOnStartup, Is.True);
        });
    }

    [Test]
    public void A_misspelled_boolean_stops_the_app_rather_than_guessing()
    {
        // "flase" should not quietly do the opposite of what you meant.
        Assert.That(() => Load(new() { ["POLL_ON_STARTUP"] = "flase" }),
            Throws.InvalidOperationException.With.Message.Contains("POLL_ON_STARTUP"));
    }

    /// <summary>
    /// Options loaded from an in-memory environment; SECRET_KEY filled in unless the test sets one.
    /// </summary>
    /// <param name="env"></param>
    private static AppOptions Load(Dictionary<string, string?> env)
    {
        if (!env.ContainsKey("SECRET_KEY")) env["SECRET_KEY"] = SqliteTestDatabase.TestSecretKey;
        return AppOptionsLoader.Load(new ConfigurationBuilder().AddInMemoryCollection(env).Build());
    }
}
