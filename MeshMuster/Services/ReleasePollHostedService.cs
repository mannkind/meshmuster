using MeshMuster.Config;
using Microsoft.Extensions.Options;

namespace MeshMuster.Services;

/// <summary>
/// Polls every source on an interval, and once at startup.
/// </summary>
public class ReleasePollHostedService : BackgroundService
{
    /// <summary>
    /// Initializes a new instance of the ReleasePollHostedService class.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    public ReleasePollHostedService(
        IServiceProvider services,
        IOptions<AppOptions> options,
        ILogger<ReleasePollHostedService> logger)
    {
        this.Services = services;
        this.Options = options;
        this.Logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = this.Options.Value;
        var interval = TimeSpan.FromHours(opts.PollIntervalHours);

        if (opts.PollOnStartup)
            await this.SyncOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await this.SyncOnceAsync(stoppingToken);
    }

    /// <summary>
    /// The provider used to scope each sync.
    /// </summary>
    private readonly IServiceProvider Services;

    /// <summary>
    /// The options used internally.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<ReleasePollHostedService> Logger;

    /// <summary>
    /// Sync every source once; a failure is logged, never thrown, or the poller dies with it.
    /// </summary>
    /// <param name="ct"></param>
    private async Task SyncOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = this.Services.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<ReleaseSyncService>();
            await sync.SyncAllAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Release poll failed");
        }
    }
}
