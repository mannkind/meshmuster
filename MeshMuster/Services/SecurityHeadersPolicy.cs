namespace MeshMuster.Services;

/// <summary>
/// The content security policy the whole site is served under.
/// </summary>
public static class SecurityHeadersPolicy
{
    public const string OpenStreetMapTiles = "https://tile.openstreetmap.org";

    /// <summary>
    /// The CDN serving alpine and leaflet. Every tag that names it also carries an SRI hash,
    /// so a swapped file fails to load rather than running.
    /// </summary>
    public const string Cdn = "https://cdn.jsdelivr.net";

    /// <summary>
    /// Add the security headers to the pipeline.
    /// </summary>
    /// <param name="app"></param>
    public static IApplicationBuilder UseMeshMusterSecurityHeaders(this IApplicationBuilder app) =>
        app.UseSecurityHeaders(policies => policies
            .AddDefaultSecurityHeaders()
            .AddContentSecurityPolicy(builder =>
            {
                builder.AddDefaultSrc().Self();

                // Alpine evaluates its expressions, so unsafe-eval stays.
                builder.AddScriptSrc().Self().UnsafeEval().From(Cdn);
                builder.AddStyleSrc().Self().UnsafeInline().From(Cdn);
                // leaflet.css names its marker images relative to itself, so they come from the CDN too.
                builder.AddImgSrc().Self().Data().From(OpenStreetMapTiles).From(Cdn);
                builder.AddConnectSrc().Self();
                builder.AddFormAction().Self();
                builder.AddFrameAncestors().None();
                builder.AddBaseUri().Self();
                builder.AddObjectSrc().None();
            })
            .AddReferrerPolicyNoReferrer());
}
