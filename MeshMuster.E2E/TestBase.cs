using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace MeshMuster.E2E;

/// <summary>
/// Starts the stubbed GitHub and the app once for the whole run.
/// </summary>
[SetUpFixture]
public class Harness
{
    public static FakeGitHubServer GitHub { get; private set; } = null!;
    public static WebAppFixture App { get; private set; } = null!;

    [OneTimeSetUp]
    public void StartAsync()
    {
        GitHub = new FakeGitHubServer();

        GitHub.SetReleases("meshcore-dev", "MeshCore",
            (1, "repeater-v1.16.0", false, ["rak4631_repeater.uf2", "xiao_repeater.uf2"]),
            (2, "repeater-v1.17.1", false, ["rak4631_repeater.uf2", "xiao_repeater.uf2"]),
            (3, "companion-v1.17.1", false, ["xiao_companion.uf2"]),
            (4, "room-server-v1.17.1", false, ["xiao_room_server.uf2"]));

        GitHub.SetReleases("mikecarper", "MeshCore",
            (5, "v1.17.1.5-halo-keymind-cascade-dev-26303793", true, ["xiao_companion.uf2"]),
            (6, "repeater-room-v1.17.1.5-halo-keymind-cascade-dev-26303793", true, ["xiao_repeater.uf2"]));

        GitHub.SetReleases("oltaco", "Adafruit_nRF52_Bootloader_OTAFIX",
            (7, "0.9.2-OTAFIX2.2-BP1.3", false, ["rak4631_bootloader.zip"]),
            (8, "0.9.2-OTAFIX2.3-BP1.4", false, ["rak4631_bootloader.zip"]));

        GitHub.SetReleases("mikecarper", "Adafruit_nRF52_Bootloader_OTAFIX",
            (9, "0.11.0-OTAFIX2.4.6", false, ["xiao_bootloader.zip"]));

        App = new WebAppFixture(GitHub.BaseUrl);
        App.WaitForReleases();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        App?.Dispose();
        GitHub?.Dispose();
    }
}

/// <summary>
/// What every browser test shares: the base URL and the two flows it drives.
/// </summary>
public abstract class TestBase : PageTest
{
    protected string BaseUrl => Harness.App.BaseUrl;

    public override BrowserNewContextOptions ContextOptions() =>
        new() { BaseURL = Harness.App.BaseUrl, IgnoreHTTPSErrors = true };

    /// <summary>
    /// Add a device through the form and return its detail URL.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="role"></param>
    /// <param name="firmwareChannel"></param>
    /// <param name="bootloaderChannel"></param>
    /// <param name="latitude"></param>
    /// <param name="longitude"></param>
    protected async Task<string> AddDeviceAsync(
        string name, string role, string? firmwareChannel = null, string? bootloaderChannel = null,
        double? latitude = null, double? longitude = null)
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", name);
        await Page.SelectOptionAsync("#Role", role);

        if (firmwareChannel is not null)
            await Page.SelectOptionAsync("#FirmwareSourceId", new SelectOptionValue { Label = firmwareChannel });

        if (bootloaderChannel is not null)
            await Page.SelectOptionAsync("#BootloaderSourceId", new SelectOptionValue { Label = bootloaderChannel });

        if (latitude is not null && longitude is not null)
        {
            await Page.FillAsync("#Latitude", latitude.Value.ToString(CultureInfo.InvariantCulture));
            await Page.FillAsync("#Longitude", longitude.Value.ToString(CultureInfo.InvariantCulture));
        }

        await Page.ClickAsync("button[type=submit]");

        try
        {
            await Expect(Page).ToHaveURLAsync(DetailUrl);
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            var errors = await Page.InnerTextAsync("body");
            throw new AssertionException(
                $"Creating '{name}' did not redirect to the detail page.\n" +
                $"URL: {Page.Url}\nPage text:\n{errors[..Math.Min(1500, errors.Length)]}");
        }

        return Page.Url;
    }

    /// <summary>
    /// Record a flash through the form.
    /// </summary>
    /// <param name="firmwareLabel"></param>
    /// <param name="bootloaderLabel"></param>
    protected async Task RecordFlashAsync(string? firmwareLabel, string? bootloaderLabel)
    {
        await Page.ClickAsync("text=Record update");

        if (firmwareLabel is not null)
            await SelectByPrefixAsync("#fw", firmwareLabel);

        if (bootloaderLabel is not null)
            await SelectByPrefixAsync("#bl", bootloaderLabel);

        await Page.ClickAsync("button[type=submit]:has-text('Record')");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
    }

    /// <summary>
    /// Pick an option by its start; they render as "1.17.1 (detail)".
    /// </summary>
    /// <param name="selector"></param>
    /// <param name="prefix"></param>
    protected async Task SelectByPrefixAsync(string selector, string prefix)
    {
        var options = await Page.Locator($"{selector} option").AllInnerTextsAsync();
        var label = options.First(o => o.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        await Page.SelectOptionAsync(selector, new SelectOptionValue { Label = label });
    }

    /// <summary>
    /// Where a successful save lands.
    /// </summary>
    protected static readonly Regex DetailUrl = new("/DeviceDetail/");
}
