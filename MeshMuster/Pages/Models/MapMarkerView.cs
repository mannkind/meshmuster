using System.Text.Json.Serialization;
using MeshMuster.Services;

namespace MeshMuster.Pages.Models;

/// <summary>
/// One pin on the map, serialized straight into the page.
/// </summary>
public record MapMarkerView
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("board")] public required string Board { get; init; }
    [JsonPropertyName("lat")] public required double Latitude { get; init; }
    [JsonPropertyName("lon")] public required double Longitude { get; init; }
    [JsonPropertyName("firmware")] public required string Firmware { get; init; }
    [JsonPropertyName("bootloader")] public required string Bootloader { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("publicKey")] public string? PublicKey { get; init; }
}

/// <summary>
/// The colour a pin gets, worst of the two channels.
/// </summary>
public static class MarkerStatus
{
    public const string Stale = "stale";
    public const string Unknown = "unknown";
    public const string Update = "update";
    public const string Current = "current";

    /// <summary>
    /// The status for a device, from its firmware and bootloader.
    /// </summary>
    /// <param name="firmware"></param>
    /// <param name="bootloader"></param>
    public static string For(DeviceUpdateStatus firmware, DeviceUpdateStatus bootloader)
    {
        UpdateState[] states = [firmware.State, bootloader.State];

        if (states.Contains(UpdateState.SourceStale))
        {
            return Stale;
        }
        else if (states.Contains(UpdateState.Unknown))
        {
            return Unknown;
        }
        else if (states.Contains(UpdateState.UpdateAvailable))
        {
            return Update;
        }

        return Current;
    }
}
