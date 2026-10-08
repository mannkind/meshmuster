namespace MeshMuster.Services.Versioning;

/// <summary>
/// Maps a source slug to the scheme that reads its tags.
/// </summary>
public class VersionSchemeRegistry
{
    public const string MeshCoreOfficial = "meshcore-official";
    public const string MeshCoreMikecarper = "meshcore-mikecarper";
    public const string OtafixOltaco = "otafix-oltaco";
    public const string OtafixMikecarper = "otafix-mikecarper";

    /// <summary>
    /// Initializes a new instance of the VersionSchemeRegistry class.
    /// </summary>
    public VersionSchemeRegistry()
    {
        this.Schemes = new Dictionary<string, IVersionScheme>(StringComparer.OrdinalIgnoreCase)
        {
            [MeshCoreOfficial] = new MeshCoreVersionScheme(),
            [MeshCoreMikecarper] = new MeshCoreVersionScheme(),
            [OtafixOltaco] = new OtafixVersionScheme(),
            [OtafixMikecarper] = new OtafixVersionScheme(),
        };
    }

    /// <summary>
    /// The scheme for a source, or null when none is registered.
    /// </summary>
    /// <param name="sourceSlug"></param>
    public IVersionScheme? For(string sourceSlug) =>
        this.Schemes.GetValueOrDefault(sourceSlug);

    /// <summary>
    /// The registered schemes, keyed by source slug.
    /// </summary>
    private readonly Dictionary<string, IVersionScheme> Schemes;
}
