using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Pages;

/// <summary>
/// The add and edit form for a device.
/// </summary>
public class DeviceEditModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the DeviceEditModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="devices"></param>
    public DeviceEditModel(AppDbContext db, DeviceService devices)
    {
        this.Db = db;
        this.Devices = devices;
    }

    /// <summary>
    /// A device already on the map, so the picker can show its neighbours.
    /// </summary>
    public record NearbyDevice
    {
        public string Name { get; init; } = string.Empty;
        public double Latitude { get; init; }
        public double Longitude { get; init; }
    }

    public bool IsNew => this.Id is null;
    public IReadOnlyList<SelectListItem> Boards { get; private set; } = [];
    public IReadOnlyList<SelectListItem> FirmwareSources { get; private set; } = [];
    public IReadOnlyList<SelectListItem> BootloaderSources { get; private set; } = [];

    public IReadOnlyList<NearbyDevice> Nearby { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Role { get; set; } = DeviceRole.Repeater;
    [BindProperty] public Guid? BoardId { get; set; }
    [BindProperty] public Guid? FirmwareSourceId { get; set; }
    [BindProperty] public Guid? BootloaderSourceId { get; set; }
    [BindProperty] public string PrivateKey { get; set; } = string.Empty;
    [BindProperty] public string AdminPassword { get; set; } = string.Empty;
    [BindProperty] public string Notes { get; set; } = string.Empty;
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public double? Latitude { get; set; }
    [BindProperty] public double? Longitude { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await this.LoadListsAsync(ct);

        if (this.Id is null) return this.Page();

        var device = await this.Db.Devices
            .Include(d => d.FirmwareStream)
            .Include(d => d.BootloaderStream)
            .FirstOrDefaultAsync(d => d.Id == this.Id, ct);

        if (device is null)
        {
            return this.NotFound();
        }

        this.Name = device.Name;
        this.Role = device.Role;
        this.BoardId = device.BoardId;

        // The form picks a source; the stream behind it follows from the role.
        this.FirmwareSourceId = device.FirmwareStream?.SourceId;
        this.BootloaderSourceId = device.BootloaderStream?.SourceId;

        this.PrivateKey = device.PrivateKey;
        this.AdminPassword = device.AdminPassword;
        this.Notes = device.Notes;
        this.IsActive = device.IsActive;
        this.Latitude = device.Latitude;
        this.Longitude = device.Longitude;
        return this.Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            this.ModelState.AddModelError(nameof(this.Name), "Name is required.");
        }

        if (!DeviceRole.All.Contains(this.Role))
        {
            this.ModelState.AddModelError(
                nameof(this.Role), $"Role must be one of: {string.Join(", ", DeviceRole.All)}."
            );
        }

        if (Coordinates.Validate(this.Latitude, this.Longitude) is { } coordinateError)
        {
            this.ModelState.AddModelError(nameof(this.Latitude), coordinateError);
        }

        if (!this.ModelState.IsValid)
        {
            await this.LoadListsAsync(ct);
            return this.Page();
        }

        var edit = new DeviceEdit
        {
            Name = this.Name,
            Role = this.Role,
            BoardId = this.BoardId,
            FirmwareSourceId = this.FirmwareSourceId,
            BootloaderSourceId = this.BootloaderSourceId,
            PrivateKey = this.PrivateKey ?? string.Empty,
            AdminPassword = this.AdminPassword ?? string.Empty,
            Notes = this.Notes ?? string.Empty,
            IsActive = this.IsActive,
            Latitude = this.Latitude,
            Longitude = this.Longitude,
        };

        if (this.Id is null)
        {
            var created = await this.Devices.CreateAsync(edit, ct);
            this.TempData["Flash"] = "Device added.";
            return this.RedirectToPage("/DeviceDetail", new { id = created.Id });
        }

        if (!await this.Devices.UpdateAsync(this.Id.Value, edit, ct))
        {
            return this.NotFound();
        }
        this.TempData["Flash"] = "Device saved.";
        return this.RedirectToPage("/DeviceDetail", new { id = this.Id.Value });
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
    /// Fill the dropdowns and the location picker.
    /// </summary>
    /// <param name="ct"></param>
    private async Task LoadListsAsync(CancellationToken ct)
    {
        this.Nearby = await this.Db.Devices
            .AsNoTracking()
            .Where(d => d.Latitude != null && d.Longitude != null && d.Id != this.Id)
            .OrderBy(d => d.Name)
            .Select(d => new NearbyDevice
            {
                Name = d.Name,
                Latitude = d.Latitude!.Value,
                Longitude = d.Longitude!.Value,
            })
            .ToListAsync(ct);

        this.Boards = await this.Db.Boards
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new SelectListItem(b.Name, b.Id.ToString()))
            .ToListAsync(ct);

        var sources = await this.Db.Sources.OrderBy(s => s.SortOrder).ToListAsync(ct);

        this.FirmwareSources = sources
            .Where(s => s.Kind == SourceKind.Firmware)
            .Select(s => new SelectListItem(s.DisplayName, s.Id.ToString()))
            .ToList();

        this.BootloaderSources = sources
            .Where(s => s.Kind == SourceKind.Bootloader)
            .Select(s => new SelectListItem(s.DisplayName, s.Id.ToString()))
            .ToList();
    }
}
