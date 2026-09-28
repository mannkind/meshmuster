using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;

namespace MeshMuster.E2E;

/// <summary>
/// Runs the real app against a temp state directory and the stubbed GitHub.
/// </summary>
public class WebAppFixture : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the WebAppFixture class.
    /// </summary>
    /// <param name="gitHubBaseUrl"></param>
    public WebAppFixture(string gitHubBaseUrl)
    {
        this.Root = Directory.CreateTempSubdirectory("meshmuster-e2e-").FullName;
        var port = FreePort();
        this.BaseUrl = $"http://127.0.0.1:{port}";

        var projectDir = FindProjectDir();

        this.Runner = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = projectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var arg in new[]
                 { "run", "--project", projectDir, "--no-launch-profile", "--no-build", "-c", "Release" })
        {
            this.Runner.StartInfo.ArgumentList.Add(arg);
        }

        this.Runner.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        this.Runner.StartInfo.Environment["LISTEN_ADDR"] = $"127.0.0.1:{port}";
        this.Runner.StartInfo.Environment["STATE_PATH"] = this.Root;
        this.Runner.StartInfo.Environment["GITHUB_API_BASE_URL"] = gitHubBaseUrl;
        this.Runner.StartInfo.Environment["FLASHER_CONFIG_URL"] = gitHubBaseUrl + "flasher-config.json";
        this.Runner.StartInfo.Environment["POLL_ON_STARTUP"] = "true";
        this.Runner.StartInfo.Environment["SECRET_KEY"] = SecretKey;

        this.Runner.Start();

        // Drained, or a full pipe blocks the app once it has logged enough.
        this.Runner.BeginOutputReadLine();
        this.Runner.BeginErrorReadLine();

        this.WaitUntilHealthy();
    }

    public string BaseUrl { get; }

    /// <summary>A throwaway key; the state directory dies with the fixture.</summary>
    public const string SecretKey = "bWVzaG11c3Rlci1lMmUtdGVzdC1rZXktLTMyYnl0ZXM=";

    public string StateDir => this.Root;

    /// <summary>
    /// Block until the startup poll has stored something.
    /// </summary>
    public void WaitForReleases()
    {
        var db = Path.Combine(this.Root, "meshmuster.db");
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Cache=Shared");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM releases";
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) return;
            }
            catch
            {
                // Database isn't there yet.
            }
            Thread.Sleep(500);
        }

        throw new TimeoutException("No releases were synced within the timeout.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (!this.Runner.HasExited)
            {
                // The tree, because dotnet run starts the app as a child.
                this.Runner.Kill(entireProcessTree: true);
                this.Runner.WaitForExit(10_000);
            }
        }
        catch
        {
            // Already gone.
        }

        this.Runner.Dispose();
        try { Directory.Delete(this.Root, recursive: true); } catch { /* temp dir */ }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The app under test.
    /// </summary>
    private readonly Process Runner;

    /// <summary>
    /// The temp state directory, deleted with the fixture.
    /// </summary>
    private readonly string Root;

    /// <summary>
    /// The solution root's MeshMuster directory.
    /// </summary>
    private static string FindProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MeshMuster.slnx")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("Could not locate the solution root.");
        return Path.Combine(dir.FullName, "MeshMuster");
    }

    /// <summary>
    /// A port the OS just handed back, so parallel runs don't collide.
    /// </summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Poll /health until it answers, or give up.
    /// </summary>
    private void WaitUntilHealthy()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (client.GetAsync($"{this.BaseUrl}/health").GetAwaiter().GetResult().IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Not up yet.
            }
            Thread.Sleep(500);
        }

        throw new TimeoutException($"MeshMuster did not become healthy at {this.BaseUrl}.");
    }
}
