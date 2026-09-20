using MeshMuster.Data;
using MeshMuster.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Services;

/// <summary>
/// The editable half of a device, as a form submits it.
/// </summary>
public record DeviceEdit
{
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = DeviceRole.Repeater;
    public Guid? BoardId { get; init; }

    /// <summary>A source, not a stream; the role picks the stream within it.</summary>
    public Guid? FirmwareSourceId { get; init; }

    /// <summary>A source, not a stream; the role picks the stream within it.</summary>
    public Guid? BootloaderSourceId { get; init; }

    public string PrivateKey { get; init; } = string.Empty;
    public string AdminPassword { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
}

/// <summary>
/// The bounds a device location has to sit within.
/// </summary>
public static class Coordinates
{
    public const double MinLatitude = -90, MaxLatitude = 90;
    public const double MinLongitude = -180, MaxLongitude = 180;

    /// <summary>Null when the pair is usable, otherwise why it isn't.</summary>
    /// <param name="latitude"></param>
    /// <param name="longitude"></param>
    public static string? Validate(double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null) return null;

        if (latitude is null || longitude is null)
            return "Latitude and longitude must be given together, or both left blank.";

        if (latitude is < MinLatitude or > MaxLatitude)
            return $"Latitude must be between {MinLatitude} and {MaxLatitude}.";

        if (longitude is < MinLongitude or > MaxLongitude)
            return $"Longitude must be between {MinLongitude} and {MaxLongitude}.";

        return null;
    }
}

/// <summary>
/// A version picked on the flash form: a known release, a typed label, or neither.
/// </summary>
public record VersionChoice
{
    /// <summary>Nothing picked, so leave that half of the device alone.</summary>
    public static readonly VersionChoice Unchanged = new();

    public Guid? ReleaseId { get; init; }

    /// <summary>Typed by hand when the release isn't one we know.</summary>
    public string Label { get; init; } = string.Empty;

    public bool IsSet => this.ReleaseId is not null || this.Label.Length > 0;
}

/// <summary>
/// One flash, as the detail page submits it.
/// </summary>
public record FlashRecord
{
    public DateOnly OccurredAt { get; init; }
    public VersionChoice Firmware { get; init; } = VersionChoice.Unchanged;
    public VersionChoice Bootloader { get; init; } = VersionChoice.Unchanged;
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// The columns the device list can sort on.
/// </summary>
public enum SortColumn { Name, Board, Firmware, Bootloader, LastUpdated }

/// <summary>
/// A managed way to read and write devices.
/// </summary>
public class DeviceService
{
    /// <summary>
    /// Initializes a new instance of the DeviceService class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="streams"></param>
    /// <param name="clock"></param>
    public DeviceService(
        AppDbContext db,
        StreamResolver streams,
        TimeProvider clock)
    {
        this.Db = db;
        this.Streams = streams;
        this.Clock = clock;
    }

    /// <summary>
    /// Create a device from an edit.
    /// </summary>
    /// <param name="edit"></param>
    /// <param name="ct"></param>
    public async Task<Device> CreateAsync(DeviceEdit edit, CancellationToken ct = default)
    {
        var now = this.Clock.GetUtcNow().UtcDateTime;
        var device = new Device { Id = Guid.NewGuid(), CreatedAt = now };
        this.Db.Devices.Add(device);
        await this.ApplyAsync(device, edit, now, ct);
        await this.Db.SaveChangesAsync(ct);
        return device;
    }

    /// <summary>
    /// Apply an edit to an existing device; false when there is no such device.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="edit"></param>
    /// <param name="ct"></param>
    public async Task<bool> UpdateAsync(Guid id, DeviceEdit edit, CancellationToken ct = default)
    {
        var device = await this.Db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return false;

        await this.ApplyAsync(device, edit, this.Clock.GetUtcNow().UtcDateTime, ct);
        await this.Db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Delete a device; false when there is no such device.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var device = await this.Db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null) return false;

        this.Db.Devices.Remove(device);
        await this.Db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Record a flash and move the device onto its new versions; null when nothing changed.
    /// </summary>
    /// <param name="deviceId"></param>
    /// <param name="record"></param>
    /// <param name="ct"></param>
    public async Task<DeviceFlash?> RecordFlashAsync(
        Guid deviceId, FlashRecord record, CancellationToken ct = default)
    {
        var device = await this.Db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null) return null;

        var firmware = await this.ResolveChoiceAsync(record.Firmware, ct);
        var bootloader = await this.ResolveChoiceAsync(record.Bootloader, ct);

        var firmwareChanged = firmware is not null && firmware.Label != device.FirmwareVersionLabel;
        var bootloaderChanged = bootloader is not null && bootloader.Label != device.BootloaderVersionLabel;

        if (!firmwareChanged && !bootloaderChanged) return null;

        var now = this.Clock.GetUtcNow().UtcDateTime;
        var flash = new DeviceFlash
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            OccurredAt = record.OccurredAt,
            Note = record.Note.Trim(),
            CreatedAt = now,
        };

        if (firmwareChanged)
        {
            flash.FirmwareFromLabel = device.FirmwareVersionLabel;
            flash.FirmwareToLabel = firmware!.Label;
            device.FirmwareVersionLabel = firmware.Label;
            device.FirmwareSortKey = firmware.SortKey;
            device.FirmwareReleaseId = firmware.ReleaseId;
        }

        if (bootloaderChanged)
        {
            flash.BootloaderFromLabel = device.BootloaderVersionLabel;
            flash.BootloaderToLabel = bootloader!.Label;
            device.BootloaderVersionLabel = bootloader.Label;
            device.BootloaderSortKey = bootloader.SortKey;
            device.BootloaderReleaseId = bootloader.ReleaseId;
        }

        device.UpdatedAt = now;
        this.Db.DeviceFlashes.Add(flash);

        // The history row and the device's new versions land together or not at all.
        await using var tx = await this.Db.Database.BeginTransactionAsync(ct);
        await this.Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return flash;
    }

    /// <summary>
    /// Every device, sorted, with its board loaded.
    /// </summary>
    /// <param name="sort"></param>
    /// <param name="descending"></param>
    /// <param name="ct"></param>
    public async Task<IReadOnlyList<Device>> ListAsync(
        SortColumn sort, bool descending, CancellationToken ct = default)
    {
        var lastFlash = this.Db.DeviceFlashes
            .GroupBy(f => f.DeviceId)
            .Select(g => new { DeviceId = g.Key, Last = g.Max(f => f.OccurredAt) });

        var query =
            from device in this.Db.Devices.Include(d => d.Board).AsNoTracking()
            join flash in lastFlash on device.Id equals flash.DeviceId into flashes
            from flash in flashes.DefaultIfEmpty()
            select new { Device = device, Last = (DateOnly?)(flash != null ? flash.Last : null) };

        // Name breaks every tie, so the order is stable between loads.
        query = sort switch
        {
            SortColumn.Name => descending
                ? query.OrderByDescending(x => x.Device.Name)
                : query.OrderBy(x => x.Device.Name),
            SortColumn.Board => descending
                ? query.OrderByDescending(x => x.Device.Board!.Name).ThenBy(x => x.Device.Name)
                : query.OrderBy(x => x.Device.Board!.Name).ThenBy(x => x.Device.Name),
            SortColumn.Firmware => descending
                ? query.OrderByDescending(x => x.Device.FirmwareSortKey).ThenBy(x => x.Device.Name)
                : query.OrderBy(x => x.Device.FirmwareSortKey).ThenBy(x => x.Device.Name),
            SortColumn.Bootloader => descending
                ? query.OrderByDescending(x => x.Device.BootloaderSortKey).ThenBy(x => x.Device.Name)
                : query.OrderBy(x => x.Device.BootloaderSortKey).ThenBy(x => x.Device.Name),
            SortColumn.LastUpdated => descending
                ? query.OrderByDescending(x => x.Last).ThenBy(x => x.Device.Name)
                : query.OrderBy(x => x.Last).ThenBy(x => x.Device.Name),
            _ => query.OrderBy(x => x.Device.Name),
        };

        var rows = await query.ToListAsync(ct);
        return rows.Select(r => r.Device).ToList();
    }

    /// <summary>
    /// When each device was last flashed; devices never flashed are absent.
    /// </summary>
    /// <param name="ct"></param>
    public async Task<IReadOnlyDictionary<Guid, DateOnly>> LastFlashedAsync(CancellationToken ct = default)
    {
        var rows = await this.Db.DeviceFlashes
            .AsNoTracking()
            .GroupBy(f => f.DeviceId)
            .Select(g => new { DeviceId = g.Key, Last = g.Max(f => f.OccurredAt) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.DeviceId, r => r.Last);
    }

    /// <summary>
    /// One device, with everything the detail page shows.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    public Task<Device?> GetAsync(Guid id, CancellationToken ct = default) =>
        this.Db.Devices
            .Include(d => d.Board)
            .Include(d => d.FirmwareStream).ThenInclude(s => s!.Source)
            .Include(d => d.BootloaderStream).ThenInclude(s => s!.Source)
            .Include(d => d.Flashes)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The resolver used to turn a source into a stream.
    /// </summary>
    private readonly StreamResolver Streams;

    /// <summary>
    /// The clock used internally.
    /// </summary>
    private readonly TimeProvider Clock;

    /// <summary>
    /// A version choice resolved against the releases we hold.
    /// </summary>
    private record ResolvedVersion
    {
        public Guid? ReleaseId { get; init; }
        public string Label { get; init; } = string.Empty;
        public string SortKey { get; init; } = string.Empty;
    }

    /// <summary>
    /// Copy an edit onto a device.
    /// </summary>
    /// <param name="device"></param>
    /// <param name="edit"></param>
    /// <param name="now"></param>
    /// <param name="ct"></param>
    private async Task ApplyAsync(Device device, DeviceEdit edit, DateTime now, CancellationToken ct)
    {
        device.Name = edit.Name.Trim();
        device.Role = edit.Role;
        device.BoardId = edit.BoardId;
        device.PrivateKey = edit.PrivateKey.Trim();
        device.AdminPassword = edit.AdminPassword.Trim();
        device.Notes = edit.Notes.Trim();
        device.IsActive = edit.IsActive;

        // Both or neither, so nothing downstream has to handle half a coordinate.
        var located = edit.Latitude is not null && edit.Longitude is not null;
        device.Latitude = located ? edit.Latitude : null;
        device.Longitude = located ? edit.Longitude : null;

        device.UpdatedAt = now;

        var firmwareStream = await this.Streams.ResolveAsync(edit.FirmwareSourceId, edit.Role, ct);
        var bootloaderStream = await this.Streams.ResolveAsync(edit.BootloaderSourceId, edit.Role, ct);

        // A new channel invalidates the pin; the old release isn't on it.
        if (device.FirmwareStreamId != firmwareStream)
        {
            device.FirmwareStreamId = firmwareStream;
            device.FirmwareReleaseId = null;
        }

        if (device.BootloaderStreamId != bootloaderStream)
        {
            device.BootloaderStreamId = bootloaderStream;
            device.BootloaderReleaseId = null;
        }
    }

    /// <summary>
    /// Turn a choice into a label and sort key; null when nothing was picked.
    /// </summary>
    /// <param name="choice"></param>
    /// <param name="ct"></param>
    private async Task<ResolvedVersion?> ResolveChoiceAsync(VersionChoice choice, CancellationToken ct)
    {
        if (!choice.IsSet) return null;

        if (choice.ReleaseId is not null)
        {
            var release = await this.Db.Releases
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == choice.ReleaseId, ct);

            if (release is not null)
            {
                return new ResolvedVersion
                {
                    ReleaseId = release.Id,
                    Label = release.VersionLabel,
                    SortKey = release.SortKey,
                };
            }
        }

        // A hand-typed label gets no sort key, which reads downstream as unknown.
        return new ResolvedVersion { Label = choice.Label.Trim() };
    }
}
