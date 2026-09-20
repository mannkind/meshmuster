using MeshMuster.Data.Entities;
using MeshMuster.Pages.Models;
using MeshMuster.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeshMuster.Pages;

/// <summary>
/// Every located device on a map, and a list of the ones that aren't.
/// </summary>
public class MapModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the MapModel class.
    /// </summary>
    /// <param name="devices"></param>
    /// <param name="status"></param>
    public MapModel(DeviceService devices, UpdateStatusService status)
    {
        this.Devices = devices;
        this.Status = status;
    }

    public IReadOnlyList<MapMarkerView> Markers { get; private set; } = [];
    public IReadOnlyList<Device> Unlocated { get; private set; } = [];
    public int NeedingAttention { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var all = await this.Devices.ListAsync(SortColumn.Name, descending: false, ct);
        var snapshot = await this.Status.LoadSnapshotAsync(ct);

        var markers = new List<MapMarkerView>();
        var unlocated = new List<Device>();

        foreach (var device in all)
        {
            if (!device.HasLocation)
            {
                unlocated.Add(device);
                continue;
            }

            var firmware = this.Status.Evaluate(snapshot, device.FirmwareStreamId,
                device.FirmwareVersionLabel, device.FirmwareSortKey, device.BoardId);
            var bootloader = this.Status.Evaluate(snapshot, device.BootloaderStreamId,
                device.BootloaderVersionLabel, device.BootloaderSortKey, device.BoardId);

            markers.Add(new MapMarkerView
            {
                Id = device.Id.ToString(),
                Name = device.Name,
                Role = DeviceRole.Label(device.Role),
                Board = device.Board?.Name ?? string.Empty,
                Latitude = device.Latitude!.Value,
                Longitude = device.Longitude!.Value,
                Firmware = Describe(firmware),
                Bootloader = Describe(bootloader),
                Status = MarkerStatus.For(firmware, bootloader),
                PublicKey = MeshCoreIdentity.DerivePublicKeyPrefix(device.PrivateKey),
            });
        }

        this.Markers = markers;
        this.Unlocated = unlocated;
        this.NeedingAttention =
            markers.Count(m => m.Status is MarkerStatus.Update or MarkerStatus.Unknown);
    }

    /// <summary>
    /// The device service used internally.
    /// </summary>
    private readonly DeviceService Devices;

    /// <summary>
    /// The update status service used internally.
    /// </summary>
    private readonly UpdateStatusService Status;

    /// <summary>
    /// One line for the popup.
    /// </summary>
    /// <param name="status"></param>
    private static string Describe(DeviceUpdateStatus status) => status.State switch
    {
        UpdateState.UpToDate => $"{status.InstalledLabel} — current",
        UpdateState.UpdateAvailable => $"{status.InstalledLabel} → {status.Latest!.VersionLabel} available",
        UpdateState.Unknown => $"{Blank(status.InstalledLabel)} — unknown",
        UpdateState.SourceStale => $"{Blank(status.InstalledLabel)} — source could not be polled",
        _ => $"{Blank(status.InstalledLabel)} — no channel set",
    };

    /// <summary>
    /// An em dash where there is no label.
    /// </summary>
    /// <param name="label"></param>
    private static string Blank(string label) => label.Length > 0 ? label : "—";
}
