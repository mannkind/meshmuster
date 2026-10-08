namespace MeshMuster.Data.Entities;

/// <summary>
/// One recorded flash of a device, and what it moved between.
/// </summary>
public class DeviceFlash
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public DateOnly OccurredAt { get; set; }
    public string FirmwareFromLabel { get; set; } = string.Empty;
    public string FirmwareToLabel { get; set; } = string.Empty;
    public string BootloaderFromLabel { get; set; } = string.Empty;
    public string BootloaderToLabel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public bool ChangedFirmware => this.FirmwareToLabel.Length > 0;
    public bool ChangedBootloader => this.BootloaderToLabel.Length > 0;
}
