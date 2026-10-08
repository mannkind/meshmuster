using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Pages.Models;
using MeshMuster.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Pages;

/// <summary>
/// The device list, grouped by role.
/// </summary>
public class IndexModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the IndexModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="devices"></param>
    /// <param name="status"></param>
    public IndexModel(
        AppDbContext db,
        DeviceService devices,
        UpdateStatusService status)
    {
        this.Db = db;
        this.Devices = devices;
        this.Status = status;
    }

    public IReadOnlyList<DeviceGroupView> Groups { get; private set; } = [];
    public int FirmwareUpdatesAvailable { get; private set; }
    public int BootloaderUpdatesAvailable { get; private set; }
    public int TotalDevices { get; private set; }
    public bool AnySourceStale { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public bool Desc { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var snapshot = await this.Status.LoadSnapshotAsync(ct);
        var lastFlashed = await this.Devices.LastFlashedAsync(ct);

        var explicitSort = ParseSort(this.Sort);
        var ordered = await this.Devices.ListAsync(explicitSort ?? SortColumn.Name, this.Desc, ct);

        var rows = ordered.Select(device => new DeviceRowView
        {
            Device = device,
            Firmware = this.Status.Evaluate(snapshot, device.FirmwareStreamId,
                device.FirmwareVersionLabel, device.FirmwareSortKey, device.BoardId),
            Bootloader = this.Status.Evaluate(snapshot, device.BootloaderStreamId,
                device.BootloaderVersionLabel, device.BootloaderSortKey, device.BoardId),
            LastFlashed = lastFlashed.TryGetValue(device.Id, out var last) ? last : null,
        });

        // With no column asked for, lead with what needs doing and the longest unflashed.
        if (explicitSort is null)
        {
            rows = rows
                .OrderByDescending(r => r.NeedsAttention)
                .ThenBy(r => r.LastFlashed ?? DateOnly.MinValue)
                .ThenBy(r => r.Device.Name, StringComparer.OrdinalIgnoreCase);
        }

        this.Groups = rows
            .GroupBy(r => r.Device.Role)
            .OrderBy(g => DeviceRole.Order(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeviceGroupView { Role = g.Key, Rows = g.ToList() })
            .ToList();

        this.TotalDevices = rows.Count();
        this.FirmwareUpdatesAvailable = rows.Count(r => r.NeedsFirmwareUpdate);
        this.BootloaderUpdatesAvailable = rows.Count(r => r.NeedsBootloaderUpdate);
        this.AnySourceStale = await this.Db.Sources
            .AnyAsync(s => s.IsEnabled && s.LastPollError != "", ct);
    }

    /// <summary>
    /// Click the active column to flip it; any other starts ascending.
    /// </summary>
    /// <param name="column"></param>
    public (string Sort, bool Desc) SortLink(string column) =>
        string.Equals(this.Sort, column, StringComparison.OrdinalIgnoreCase) && !this.Desc
            ? (column, true)
            : (column, false);

    /// <summary>
    /// The arrow beside the column currently sorted on.
    /// </summary>
    /// <param name="column"></param>
    public string SortIndicator(string column) =>
        !string.Equals(this.Sort, column, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : this.Desc ? " ↓" : " ↑";

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The device service used internally.
    /// </summary>
    private readonly DeviceService Devices;

    /// <summary>
    /// The update status service used internally.
    /// </summary>
    private readonly UpdateStatusService Status;

    /// <summary>
    /// The column a query string names, or null for the default order.
    /// </summary>
    /// <param name="value"></param>
    private static SortColumn? ParseSort(string? value) => value?.ToLowerInvariant() switch
    {
        "name" => SortColumn.Name,
        "board" => SortColumn.Board,
        "firmware" => SortColumn.Firmware,
        "bootloader" => SortColumn.Bootloader,
        "updated" => SortColumn.LastUpdated,
        _ => null,
    };
}
