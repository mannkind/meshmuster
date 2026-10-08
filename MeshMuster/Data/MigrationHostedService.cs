using MeshMuster.Config;
using Microsoft.Extensions.Options;

namespace MeshMuster.Data;

/// <summary>
/// Migrates on startup, ahead of everything that reads the database.
/// </summary>
public class MigrationHostedService : IHostedService
{
    /// <summary>
    /// Initializes a new instance of the MigrationHostedService class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="runnerLogger"></param>
    public MigrationHostedService(
        IOptions<AppOptions> options,
        ILogger<MigrationRunner> runnerLogger)
    {
        this.Options = options;
        this.RunnerLogger = runnerLogger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
        var runner = new MigrationRunner(
            SqliteConfiguration.ConnectionString(this.Options.Value),
            directory, this.RunnerLogger
        );
        return runner.RunAsync(ct);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// The options used internally.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The logger handed to the runner this builds.
    /// </summary>
    private readonly ILogger<MigrationRunner> RunnerLogger;
}
