namespace MeshMuster.Data.Entities;

/// <summary>
/// A node on the mesh, and the firmware and bootloader it is running.
/// </summary>
public class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = DeviceRole.Repeater;
    public Guid? BoardId { get; set; }
    public Board? Board { get; set; }

    public Guid? FirmwareStreamId { get; set; }
    public ReleaseStream? FirmwareStream { get; set; }
    public Guid? FirmwareReleaseId { get; set; }
    public Release? FirmwareRelease { get; set; }
    public string FirmwareVersionLabel { get; set; } = string.Empty;
    public string FirmwareSortKey { get; set; } = string.Empty;

    public Guid? BootloaderStreamId { get; set; }
    public ReleaseStream? BootloaderStream { get; set; }
    public Guid? BootloaderReleaseId { get; set; }
    public Release? BootloaderRelease { get; set; }
    public string BootloaderVersionLabel { get; set; } = string.Empty;
    public string BootloaderSortKey { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool HasLocation => this.Latitude is not null && this.Longitude is not null;

    public string PrivateKey { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<DeviceFlash> Flashes { get; set; } = [];
}
