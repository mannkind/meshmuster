using MeshMuster.Data.Entities;
using MeshMuster.Services;

namespace MeshMuster.Pages.Models;

/// <summary>
/// One device as the list renders it, already judged against both channels.
/// </summary>
public record DeviceRowView
{
    public Device Device { get; init; } = null!;
    public DeviceUpdateStatus Firmware { get; init; } = new();
    public DeviceUpdateStatus Bootloader { get; init; } = new();

    /// <summary>Null for a device that has never been flashed.</summary>
    public DateOnly? LastFlashed { get; init; }

    public string? PublicKeyPrefix => MeshCoreIdentity.DerivePublicKeyPrefix(this.Device.PrivateKey);

    public bool NeedsFirmwareUpdate => this.Firmware.State == UpdateState.UpdateAvailable;
    public bool NeedsBootloaderUpdate => this.Bootloader.State == UpdateState.UpdateAvailable;
    public bool NeedsAttention => this.Firmware.NeedsAttention || this.Bootloader.NeedsAttention;
}

/// <summary>
/// The devices of one role, as the list groups them.
/// </summary>
public record DeviceGroupView
{
    public string Role { get; init; } = string.Empty;
    public IReadOnlyList<DeviceRowView> Rows { get; init; } = [];
}

/// <summary>
/// What the device table partial is handed.
/// </summary>
public record DeviceTableView
{
    public IndexModel Page { get; init; } = null!;
    public IReadOnlyList<DeviceRowView> Rows { get; init; } = [];
}

/// <summary>
/// A secret shown behind a reveal.
/// </summary>
public record SecretView
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
