namespace MeshMuster.Data.Entities;

/// <summary>
/// The device roles, and how they are ordered and labelled.
/// </summary>
public static class DeviceRole
{
    public const string Repeater = "repeater";
    public const string Companion = "companion";
    public const string Room = "room";
    public const string Sensor = "sensor";
    public const string Kiss = "kiss";

    // Also the order the device list groups by.
    public static readonly IReadOnlyList<string> All =
        [Repeater, Companion, Room, Sensor, Kiss];

    /// <summary>
    /// Sort position of a role; unknown roles land last.
    /// </summary>
    /// <param name="role"></param>
    public static int Order(string role) => Ordering.TryGetValue(role, out var i) ? i : All.Count;

    /// <summary>
    /// The role as shown to a person.
    /// </summary>
    /// <param name="role"></param>
    public static string Label(string role) => role switch
    {
        Repeater => "Repeater",
        Companion => "Companion",
        Room => "Room",
        Sensor => "Sensor",
        Kiss => "KISS",
        _ => role,
    };

    /// <summary>
    /// The role as shown to a person, plural.
    /// </summary>
    /// <param name="role"></param>
    public static string PluralLabel(string role) => role switch
    {
        Repeater => "Repeaters",
        Companion => "Companions",
        Room => "Rooms",
        Sensor => "Sensors",
        Kiss => "KISS",
        _ => role,
    };

    /// <summary>
    /// Role to sort position, built once from All.
    /// </summary>
    private static readonly Dictionary<string, int> Ordering =
        All.Select((role, index) => (role, index)).ToDictionary(x => x.role, x => x.index);
}

/// <summary>
/// One release channel within a source, usually a device role.
/// </summary>
public class ReleaseStream
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Source? Source { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Null on the bootloader streams, which serve every role.</summary>
    public string? DeviceRole { get; set; }

    public ICollection<Release> Releases { get; set; } = [];
}
