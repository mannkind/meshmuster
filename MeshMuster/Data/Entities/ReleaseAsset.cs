namespace MeshMuster.Data.Entities;

/// <summary>
/// A downloadable file attached to a release.
/// </summary>
public class ReleaseAsset
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public Release? Release { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
