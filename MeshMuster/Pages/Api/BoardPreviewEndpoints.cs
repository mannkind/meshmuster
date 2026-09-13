using MeshMuster.Services;

namespace MeshMuster.Pages.Api;

/// <summary>
/// Feeds the live match preview on the boards page.
/// </summary>
public static class BoardPreviewEndpoints
{
    /// <summary>
    /// Map the board preview endpoint.
    /// </summary>
    /// <param name="app"></param>
    public static void MapBoardPreviewEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/boards/{id:guid}/preview",
            async (Guid id, BoardService boards, CancellationToken ct) =>
                Results.Ok(await boards.PreviewAsync(id, ct)));
}
