namespace MeshMuster.Data.Entities;

/// <summary>
/// A hardware board a device can be built on.
/// </summary>
public class Board
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<BoardAssetPattern> AssetPatterns { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
}
