namespace MeshMuster.Data.Entities;

/// <summary>
/// The filename pattern that picks a board's asset out of one source's releases.
/// </summary>
public class BoardAssetPattern
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public Board? Board { get; set; }
    public Guid SourceId { get; set; }
    public Source? Source { get; set; }
    public string Pattern { get; set; } = string.Empty;
}
