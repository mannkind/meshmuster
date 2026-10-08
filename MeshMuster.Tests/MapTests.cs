using MeshMuster.Data.Entities;
using MeshMuster.Pages.Models;
using MeshMuster.Services;

namespace MeshMuster.Tests;

/// <summary>
/// The coordinate bounds, and what half a pair does.
/// </summary>
public class CoordinateValidationTests
{
    [Test]
    public void Both_blank_is_fine() =>
        Assert.That(Coordinates.Validate(null, null), Is.Null);

    [Test]
    public void A_usable_pair_is_fine() =>
        Assert.That(Coordinates.Validate(47.6062, -122.3321), Is.Null);

    [TestCase(47.6062, null)]
    [TestCase(null, -122.3321)]
    public void Half_a_pair_is_rejected(double? lat, double? lon) =>
        // Taking half a pair would drop the device on the equator.
        Assert.That(Coordinates.Validate(lat, lon), Does.Contain("together"));

    [TestCase(90.1)]
    [TestCase(-90.1)]
    public void Latitude_must_be_on_the_globe(double lat) =>
        Assert.That(Coordinates.Validate(lat, 0), Does.Contain("Latitude"));

    [TestCase(180.1)]
    [TestCase(-180.1)]
    public void Longitude_must_be_on_the_globe(double lon) =>
        Assert.That(Coordinates.Validate(0, lon), Does.Contain("Longitude"));

    [TestCase(90)]
    [TestCase(-90)]
    public void The_poles_are_valid(double lat) =>
        Assert.That(Coordinates.Validate(lat, 0), Is.Null);

    [Test]
    public void Null_island_is_valid() =>
        Assert.That(Coordinates.Validate(0, 0), Is.Null);
}

/// <summary>
/// The pin colour, which takes the worst of the two channels.
/// </summary>
public class MarkerStatusTests
{
    [Test]
    public void Both_current_is_current() =>
        Assert.That(MarkerStatus.For(Status(UpdateState.UpToDate), Status(UpdateState.UpToDate)),
            Is.EqualTo(MarkerStatus.Current));

    [Test]
    public void An_update_on_either_component_shows_as_update()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MarkerStatus.For(Status(UpdateState.UpdateAvailable), Status(UpdateState.UpToDate)),
                Is.EqualTo(MarkerStatus.Update));
            Assert.That(MarkerStatus.For(Status(UpdateState.UpToDate), Status(UpdateState.UpdateAvailable)),
                Is.EqualTo(MarkerStatus.Update));
        });
    }

    [Test]
    public void Unknown_outranks_an_available_update() =>
        Assert.That(MarkerStatus.For(Status(UpdateState.Unknown), Status(UpdateState.UpdateAvailable)),
            Is.EqualTo(MarkerStatus.Unknown));

    [Test]
    public void A_stale_source_outranks_everything() =>
        Assert.That(MarkerStatus.For(Status(UpdateState.SourceStale), Status(UpdateState.Unknown)),
            Is.EqualTo(MarkerStatus.Stale));

    [Test]
    public void No_channel_set_is_not_treated_as_current() =>
        Assert.That(MarkerStatus.For(Status(UpdateState.NoChannelSet), Status(UpdateState.NoChannelSet)),
            Is.EqualTo(MarkerStatus.Current));

    /// <summary>
    /// A status in one state, with whatever else that state implies.
    /// </summary>
    /// <param name="state"></param>
    private static DeviceUpdateStatus Status(UpdateState state) =>
        new()
        {
            State = state,
            InstalledLabel = "1.0",
            SourceName = "src",
            StaleReason = state == UpdateState.SourceStale ? "boom" : null,
        };
}

/// <summary>
/// Whether a device counts as located.
/// </summary>
public class DeviceLocationTests
{
    [Test]
    public void HasLocation_needs_both_halves()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new Device { Latitude = 47.6, Longitude = -122.3 }.HasLocation, Is.True);
            Assert.That(new Device { Latitude = 47.6 }.HasLocation, Is.False);
            Assert.That(new Device { Longitude = -122.3 }.HasLocation, Is.False);
            Assert.That(new Device().HasLocation, Is.False);
        });
    }
}
