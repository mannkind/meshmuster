using MeshMuster.Data;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Services;

/// <summary>
/// Picks the stream within a source that serves a device role.
/// </summary>
public class StreamResolver
{
    /// <summary>
    /// Initializes a new instance of the StreamResolver class.
    /// </summary>
    /// <param name="db"></param>
    public StreamResolver(AppDbContext db)
    {
        this.Db = db;
    }

    /// <summary>
    /// The stream for a role, or null when the source has none.
    /// </summary>
    /// <param name="sourceId"></param>
    /// <param name="role"></param>
    /// <param name="ct"></param>
    public async Task<Guid?> ResolveAsync(Guid? sourceId, string role, CancellationToken ct = default)
    {
        if (sourceId is null) return null;

        var candidates = await this.Db.Streams
            .Where(s => s.SourceId == sourceId)
            .ToListAsync(ct);

        // Exact role wins, then the role-agnostic stream the bootloader repos use.
        var match = candidates.FirstOrDefault(s => s.DeviceRole == role)
                    ?? candidates.FirstOrDefault(s => s.DeviceRole is null);

        return match?.Id;
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
