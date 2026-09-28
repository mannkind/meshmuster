using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Pages;

/// <summary>
/// One device, its update status, and the form that records a flash.
/// </summary>
public class DeviceDetailModel : PageModel
{
    public const string OtherChoice = "other";

    /// <summary>
    /// Initializes a new instance of the DeviceDetailModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="devices"></param>
    /// <param name="status"></param>
    public DeviceDetailModel(
        AppDbContext db,
        DeviceService devices,
        UpdateStatusService status)
    {
        this.Db = db;
        this.Devices = devices;
        this.Status = status;
    }

    public Device Device { get; private set; } = null!;
    public DeviceUpdateStatus Firmware { get; private set; } = null!;
    public DeviceUpdateStatus Bootloader { get; private set; } = null!;
    public IReadOnlyList<DeviceFlash> History { get; private set; } = [];
    public IReadOnlyList<Release> FirmwareChoices { get; private set; } = [];
    public IReadOnlyList<Release> BootloaderChoices { get; private set; } = [];
    public DateOnly Today { get; private set; }

    [BindProperty(SupportsGet = true)] public bool Record { get; set; }

    public string? PublicKey => MeshCoreIdentity.DerivePublicKey(this.Device?.PrivateKey);
    public string? PublicKeyPrefix => MeshCoreIdentity.DerivePublicKeyPrefix(this.Device?.PrivateKey);

    [BindProperty] public string FirmwareChoice { get; set; } = string.Empty;
    [BindProperty] public string FirmwareLabel { get; set; } = string.Empty;
    [BindProperty] public string BootloaderChoice { get; set; } = string.Empty;
    [BindProperty] public string BootloaderLabel { get; set; } = string.Empty;
    [BindProperty] public DateOnly OccurredAt { get; set; }
    [BindProperty] public string Note { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!await this.LoadAsync(id, ct)) return this.NotFound();
        return this.Page();
    }

    public async Task<IActionResult> OnPostFlashAsync(Guid id, CancellationToken ct)
    {
        var record = new FlashRecord
        {
            OccurredAt = this.OccurredAt == default
                ? DateOnly.FromDateTime(DateTime.UtcNow)
                : this.OccurredAt,
            Firmware = ParseChoice(this.FirmwareChoice, this.FirmwareLabel),
            Bootloader = ParseChoice(this.BootloaderChoice, this.BootloaderLabel),
            Note = this.Note ?? string.Empty,
        };

        var flash = await this.Devices.RecordFlashAsync(id, record, ct);

        this.TempData["Flash"] = flash is null
            ? "Nothing changed, so no update was recorded."
            : "Update recorded.";

        return this.RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        if (!await this.Devices.DeleteAsync(id, ct)) return this.NotFound();
        this.TempData["Flash"] = "Device deleted.";
        return this.RedirectToPage("/Index");
    }

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
    /// Read the version dropdown; OtherChoice means the label beside it was typed.
    /// </summary>
    /// <param name="choice"></param>
    /// <param name="label"></param>
    private static VersionChoice ParseChoice(string? choice, string? label)
    {
        if (string.IsNullOrEmpty(choice))
        {
            return VersionChoice.Unchanged;
        }

        if (string.Equals(choice, OtherChoice, StringComparison.Ordinal))
        {
            return new VersionChoice { Label = label ?? string.Empty };
        }

        return Guid.TryParse(choice, out var releaseId)
            ? new VersionChoice { ReleaseId = releaseId }
            : VersionChoice.Unchanged;
    }

    /// <summary>
    /// Load the device and everything the page shows; false when there is no such device.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    private async Task<bool> LoadAsync(Guid id, CancellationToken ct)
    {
        var device = await this.Devices.GetAsync(id, ct);
        if (device is null)
        {
            return false;
        }

        this.Device = device;
        this.Today = DateOnly.FromDateTime(DateTime.UtcNow);
        this.OccurredAt = this.Today;

        var snapshot = await this.Status.LoadSnapshotAsync(ct);
        this.Firmware = this.Status.Evaluate(snapshot, device.FirmwareStreamId,
            device.FirmwareVersionLabel, device.FirmwareSortKey, device.BoardId);
        this.Bootloader = this.Status.Evaluate(snapshot, device.BootloaderStreamId,
            device.BootloaderVersionLabel, device.BootloaderSortKey, device.BoardId);

        this.History = device.Flashes
            .OrderByDescending(f => f.OccurredAt)
            .ThenByDescending(f => f.CreatedAt)
            .ToList();

        this.FirmwareChoices = await this.ChoicesAsync(device.FirmwareStreamId, ct);
        this.BootloaderChoices = await this.ChoicesAsync(device.BootloaderStreamId, ct);
        return true;
    }

    /// <summary>
    /// The releases offered in a version dropdown.
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="ct"></param>
    private async Task<IReadOnlyList<Release>> ChoicesAsync(Guid? streamId, CancellationToken ct)
    {
        if (streamId is null)
        {
            return [];
        }

        var includePrereleases = await this.Db.Streams
            .Where(s => s.Id == streamId)
            .Select(s => s.Source!.IncludePrereleases)
            .FirstOrDefaultAsync(ct);

        return await this.Status.ReleasesForStreamAsync(streamId.Value, includePrereleases, ct);
    }
}
