namespace MeshMuster.Pages.Api;

/// <summary>
/// The health endpoint the container probe hits.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Map the health endpoint.
    /// </summary>
    /// <param name="app"></param>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app) =>
        app.MapHealthChecks("/health");
}
