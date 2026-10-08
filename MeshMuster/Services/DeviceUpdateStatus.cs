using MeshMuster.Data.Entities;

namespace MeshMuster.Services;

/// <summary>
/// Where a device stands against its channel.
/// </summary>
public enum UpdateState
{
    /// <summary>No channel picked, so there's nothing to compare against.</summary>
    NoChannelSet,

    /// <summary>Installed version doesn't match any known release.</summary>
    Unknown,

    /// <summary>The source last failed to poll, so the comparison is old.</summary>
    SourceStale,

    UpToDate,
    UpdateAvailable,
}

/// <summary>
/// One device's firmware or bootloader, judged against its channel.
/// </summary>
public record DeviceUpdateStatus
{
    /// <summary>
    /// A status for a device with no channel set.
    /// </summary>
    /// <param name="installedLabel"></param>
    public static DeviceUpdateStatus None(string installedLabel) =>
        new()
        {
            State = UpdateState.NoChannelSet,
            InstalledLabel = installedLabel,
        };

    public UpdateState State { get; init; } = UpdateState.NoChannelSet;

    public string InstalledLabel { get; init; } = string.Empty;

    /// <summary>The newest release on the channel, when there is one.</summary>
    public Release? Latest { get; init; }

    public int ReleasesBehind { get; init; }

    /// <summary>The file to flash, when the board carries a pattern that picks one.</summary>
    public ReleaseAsset? MatchedAsset { get; init; }

    public string SourceName { get; init; } = string.Empty;

    /// <summary>Why the source is stale, straight from the failed poll.</summary>
    public string? StaleReason { get; init; }

    public bool NeedsAttention =>
        this.State is UpdateState.UpdateAvailable or UpdateState.Unknown;
}
