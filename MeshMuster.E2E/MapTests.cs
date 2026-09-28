using Microsoft.Playwright;

namespace MeshMuster.E2E;

[Parallelizable(ParallelScope.None)]
/// <summary>
/// The map: pins, popups, and the picker on the edit form.
/// </summary>
public class MapTests : TestBase
{
    [Test]
    public async Task A_located_device_gets_a_marker()
    {
        var url = await AddDeviceAsync("e2e-map-located", "repeater", "MeshCore (official)",
            latitude: 47.6062, longitude: -122.3321);

        await Page.GotoAsync("/Map");

        // Circles rather than pins, so the map needs none of Leaflet's images.
        var id = url[(url.LastIndexOf('/') + 1)..];
        await Expect(Page.Locator($"path[data-device='{id}']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#map")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_device_without_coordinates_is_listed_rather_than_silently_missing()
    {
        await AddDeviceAsync("e2e-map-unlocated", "companion");

        await Page.GotoAsync("/Map");

        await Expect(Page.Locator("text=No location")).ToBeVisibleAsync();
        await Expect(Page.Locator("a:has-text('e2e-map-unlocated')")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_marker_popup_shows_the_device_and_links_to_it()
    {
        var url = await AddDeviceAsync("e2e-map-popup", "repeater", "MeshCore (official)",
            latitude: 51.5074, longitude: -0.1278);
        var id = url[(url.LastIndexOf('/') + 1)..];

        await Page.GotoAsync("/Map");
        // By id, because other tests add markers to the same app.
        await Page.Locator($"path[data-device='{id}']").ClickAsync();

        var popup = Page.Locator(".leaflet-popup-content");
        await Expect(popup).ToContainTextAsync("e2e-map-popup");
        await Expect(popup.Locator($"a[href='/DeviceDetail/{id}']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Half_a_coordinate_is_rejected_rather_than_placing_the_device_on_the_equator()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-map-half");
        await Page.FillAsync("#Latitude", "47.6062");
        await Page.ClickAsync("button[type=submit]");

        // Still on the form, with the reason on screen.
        await Expect(Page.Locator("body")).ToContainTextAsync("together");
        Assert.That(Page.Url, Does.Contain("/DeviceEdit"));
    }

    [Test]
    public async Task An_out_of_range_latitude_is_rejected()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-map-range");
        await Page.FillAsync("#Latitude", "120");
        await Page.FillAsync("#Longitude", "0");
        await Page.ClickAsync("button[type=submit]");

        await Expect(Page.Locator("body")).ToContainTextAsync("Latitude");
        Assert.That(Page.Url, Does.Contain("/DeviceEdit"));
    }

    [Test]
    public async Task Tiles_and_the_cdn_are_the_only_external_origins_the_page_requests()
    {
        await AddDeviceAsync("e2e-map-csp", "repeater", "MeshCore (official)",
            latitude: 40.7128, longitude: -74.0060);

        var external = new List<string>();
        Page.Request += (_, request) =>
        {
            var host = new Uri(request.Url).Host;
            if (!host.Contains("127.0.0.1") && !host.Contains("localhost")) external.Add(host);
        };

        await Page.GotoAsync("/Map");
        await Page.WaitForTimeoutAsync(1500);

        Assert.That(external.Distinct(), Is.SubsetOf(new[] { "tile.openstreetmap.org", "cdn.jsdelivr.net" }),
            $"Unexpected external origins: {string.Join(", ", external.Distinct())}");
    }

    [Test]
    public async Task Clicking_the_picker_fills_the_coordinate_fields()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-pick");
        await Expect(Page.Locator("#location-picker")).ToBeVisibleAsync();

        var box = await Page.Locator("#location-picker").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);

        // The map writes into the same fields you could have typed into.
        await Expect(Page.Locator("#Latitude")).Not.ToHaveValueAsync("");
        await Expect(Page.Locator("#Longitude")).Not.ToHaveValueAsync("");
    }

    [Test]
    public async Task A_picked_position_saves_and_comes_back_on_the_map()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-pick-save");
        var box = await Page.Locator("#location-picker").BoundingBoxAsync();
        await Page.Mouse.ClickAsync(box!.X + box.Width / 3, box.Y + box.Height / 3);

        var lat = await Page.Locator("#Latitude").InputValueAsync();
        Assert.That(lat, Is.Not.Empty);

        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
        var id = Page.Url[(Page.Url.LastIndexOf('/') + 1)..];

        await Page.GotoAsync("/Map");
        await Expect(Page.Locator($"path[data-device='{id}']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Typing_a_coordinate_still_wins()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-pick-typed");
        await Page.FillAsync("#Latitude", "47.6062");
        await Page.FillAsync("#Longitude", "-122.3321");
        await Page.Locator("#Longitude").BlurAsync();

        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        // The picker must not round-trip a typed value into something else.
        await Page.GotoAsync(Page.Url.Replace("/DeviceDetail/", "/DeviceEdit/"));
        Assert.Multiple(async () =>
        {
            Assert.That(await Page.Locator("#Latitude").InputValueAsync(), Does.StartWith("47.6062"));
            Assert.That(await Page.Locator("#Longitude").InputValueAsync(), Does.StartWith("-122.3321"));
        });
    }

    [Test]
    public async Task Clearing_removes_the_position()
    {
        var url = await AddDeviceAsync("e2e-pick-clear", "repeater", latitude: 40.0, longitude: -80.0);

        await Page.GotoAsync(url.Replace("/DeviceDetail/", "/DeviceEdit/"));
        await Page.ClickAsync("#location-clear");

        Assert.Multiple(async () =>
        {
            Assert.That(await Page.Locator("#Latitude").InputValueAsync(), Is.Empty);
            Assert.That(await Page.Locator("#Longitude").InputValueAsync(), Is.Empty);
        });

        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
        var id = Page.Url[(Page.Url.LastIndexOf('/') + 1)..];

        await Page.GotoAsync("/Map");
        await Expect(Page.Locator($"path[data-device='{id}']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_real_trackpad_pinch_zooms_the_picker()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Expect(Page.Locator("#location-picker .leaflet-tile").First).ToBeVisibleAsync();

        async Task<int?> TileZoomAsync() => await Page.EvaluateAsync<int?>(
            """
            () => {
                const t = document.querySelector('#location-picker .leaflet-tile');
                const m = t && t.src.match(/\/(\d+)\/\d+\/\d+\.png/);
                return m ? Number(m[1]) : null;
            }
            """);

        await Page.Locator("#location-picker").ClickAsync();
        await Page.WaitForTimeoutAsync(600);

        var before = await TileZoomAsync();
        Assert.That(before, Is.Not.Null, "the picker rendered no tile to read a zoom level from");

        // macOS sends roughly this much per event while you spread two fingers.
        await Page.EvaluateAsync(
            """
            async () => {
                const el = document.getElementById('location-picker');
                for (let i = 0; i < 20; i++) {
                    el.dispatchEvent(new WheelEvent('wheel', {
                        deltaY: -8, ctrlKey: true, clientX: 100, clientY: 100,
                        bubbles: true, cancelable: true,
                    }));
                    await new Promise(r => setTimeout(r, 25));
                }
            }
            """);
        await Page.WaitForTimeoutAsync(1200);

        Assert.That(await TileZoomAsync(), Is.GreaterThanOrEqualTo(before!.Value + 2),
            "a pinch made of small deltas should cover real ground, not creep");
    }

    [Test]
    public async Task Wheeling_over_an_unclicked_picker_scrolls_the_page()
    {
        await Page.GotoAsync("/DeviceEdit");
        await Expect(Page.Locator("#location-picker .leaflet-tile").First).ToBeVisibleAsync();

        async Task<string?> ZoomAsync() => await Page.EvaluateAsync<string?>(
            @"() => {
                const t = document.querySelector('#location-picker .leaflet-tile');
                const m = t && t.src.match(/\/(\d+)\/\d+\/\d+\.png/);
                return m ? m[1] : null;
            }");

        var before = await ZoomAsync();

        await Page.Locator("#location-picker").HoverAsync();
        await Page.Mouse.WheelAsync(0, 300);
        await Page.WaitForTimeoutAsync(600);

        Assert.Multiple(async () =>
        {
            Assert.That(await ZoomAsync(), Is.EqualTo(before), "an unclicked map should not zoom");
            Assert.That(await Page.EvaluateAsync<int>("() => window.scrollY"), Is.GreaterThan(0));
        });
    }
}
