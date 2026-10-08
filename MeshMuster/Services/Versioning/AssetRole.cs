using MeshMuster.Data.Entities;

namespace MeshMuster.Services.Versioning;

/// <summary>
/// Reads the device role out of an asset filename.
/// </summary>
public static class AssetRole
{
    /// <summary>
    /// The role the filename names, or null when it names none.
    /// </summary>
    /// <param name="assetName"></param>
    public static string? Of(string assetName) =>
        // room_server first; "room" alone would also catch the repeater-room builds.
        Has(assetName, "room_server") ? DeviceRole.Room
        : Has(assetName, "companion") ? DeviceRole.Companion
        : Has(assetName, "repeater") ? DeviceRole.Repeater
        : Has(assetName, "sensor") ? DeviceRole.Sensor
        : Has(assetName, "kiss") ? DeviceRole.Kiss
        : null;

    /// <summary>
    /// Whether the name carries a token, ignoring case.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="token"></param>
    private static bool Has(string name, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
