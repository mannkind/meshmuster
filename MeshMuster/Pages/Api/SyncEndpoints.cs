using MeshMuster.Services;

namespace MeshMuster.Pages.Api;

/// <summary>
/// The sync endpoint, for driving a poll from outside the UI.
/// </summary>
public static class SyncEndpoints
{
    /// <summary>
    /// Map the sync endpoint.
    /// </summary>
    /// <param name="app"></param>
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/sync", async (ReleaseSyncService sync, CancellationToken ct) =>
        {
            var outcomes = await sync.SyncAllAsync(ct);
            return Results.Ok(new
            {
                ok = outcomes.All(o => o.Succeeded),
                results = outcomes.Select(o => new
                {
                    source = o.SourceSlug,
                    added = o.Added,
                    notModified = o.NotModified,
                    error = o.Error,
                }),
            });
        });
}
