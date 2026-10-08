using System.Globalization;
using Microsoft.Playwright;

namespace MeshMuster.E2E;

[Explicit("Generates README images, not assertions.")]
[Parallelizable(ParallelScope.None)]
/// <summary>
/// Drives the app through a demo dataset and writes the README images.
/// </summary>
public class Screenshots : TestBase
{
    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = Harness.App.BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = Width, Height = 900 },
            DeviceScaleFactor = 2,
            ColorScheme = ColorScheme.Light,
        };

    [Test]
    public async Task Capture()
    {
        var outDir = Path.Combine(SolutionRoot(), "docs", "screenshots");
        Directory.CreateDirectory(outDir);

        foreach (var board in Boards) await AddBoardAsync(board);
        foreach (var device in Devices) await AddDeviceAsync(device);

        await Page.GotoAsync("/");
        await Expect(Page.Locator("h2:has-text('Repeaters')")).ToBeVisibleAsync();

        var capitolHill = await FlashAsync(
            "Capitol Hill", "2025-11-02", null, "OTAFIX 2.2", "OTA bootloader fix");
        await FlashAsync("Capitol Hill", "2026-04-18", "1.16.0", null, "roof visit, new antenna");
        await FlashAsync("Alki Point", "2026-06-02", "1.17.1", "OTAFIX 2.3", "");
        await FlashAsync("Gas Works", "2026-05-20", "1.17.1.5", "OTAFIX 2.4.6", "");
        await FlashAsync("Bainbridge Ferry", "2026-01-09", "1.16.0", null, "");
        await FlashAsync("Pocket companion", "2026-06-02", "1.17.1", null, "");
        await FlashAsync("Truck", "2026-06-11", "1.17.1.5", "OTAFIX 2.4.6", "");
        await UnknownFlashAsync("Tacoma Dome", "2025-08-14", "1.14.0", "OTAFIX 2.2");

        await ShotAsync("/", Path.Combine(outDir, "devices.png"));
        await ShotAsync(capitolHill, Path.Combine(outDir, "device.png"));
        await ShotAsync("/Map", Path.Combine(outDir, "map.jpg"), SettleMapAsync);
        await ShotAsync("/Sources", Path.Combine(outDir, "sources.png"));
        await ShotAsync("/Boards", Path.Combine(outDir, "boards.png"), PreviewFirstBoardAsync);

        TestContext.Out.WriteLine($"Screenshots written to {outDir}");
    }

    /// <summary>
    /// Wait for the basemap; a half-loaded tile grid photographs badly.
    /// </summary>
    private async Task SettleMapAsync()
    {
        await Expect(Page.Locator("path[data-device]").First).ToBeVisibleAsync();
        // Let the basemap finish drawing; a half-loaded tile grid photographs badly.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(2500);
    }

    /// <summary>
    /// Open the first board's match preview, so the shot shows it populated.
    /// </summary>
    private async Task PreviewFirstBoardAsync()
    {
        var preview = Page.Locator("button:has-text('preview matches')").First;
        if (await preview.CountAsync() == 0) return;

        await preview.ClickAsync();
        await Page.WaitForTimeoutAsync(1500);
    }

    /// <summary>
    /// Load a page, size the viewport to its content, and shoot it.
    /// </summary>
    /// <param name="url"></param>
    /// <param name="file"></param>
    /// <param name="settle"></param>
    private async Task ShotAsync(string url, string file, Func<Task>? settle = null)
    {
        await Page.SetViewportSizeAsync(Width, ProbeHeight);
        await Page.GotoAsync(url);
        if (settle is not null) await settle();

        var height = Math.Clamp(await ContentHeightAsync(), 360, 2400);
        if (height != ProbeHeight)
        {
            await Page.SetViewportSizeAsync(Width, height);
            await Page.GotoAsync(url);
            if (settle is not null) await settle();
        }

        await Page.Mouse.MoveAsync(0, 0);
        await Page.EvaluateAsync("() => document.fonts.ready");
        await Page.WaitForTimeoutAsync(250);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = file,
            FullPage = true,
            Quality = file.EndsWith(".jpg", StringComparison.Ordinal) ? 85 : null,
        });
    }

    /// <summary>
    /// How tall the content actually is, so nothing is cut off.
    /// </summary>
    private Task<int> ContentHeightAsync() => Page.EvaluateAsync<int>("""
        () => {
            const main = document.querySelector('main') ?? document.body;
            return Math.ceil(main.getBoundingClientRect().bottom + window.scrollY) + 20;
        }
        """);

    /// <summary>
    /// Record a flash against a device by name, for the demo history.
    /// </summary>
    /// <param name="device"></param>
    /// <param name="date"></param>
    /// <param name="firmware"></param>
    /// <param name="bootloader"></param>
    /// <param name="note"></param>
    private async Task<string> FlashAsync(
        string device, string date, string? firmware, string? bootloader, string note)
    {
        await Page.GotoAsync("/");
        await Page.ClickAsync($"a:has-text('{device}')");
        await Expect(Page.Locator("h1")).ToHaveTextAsync(device);

        await Page.ClickAsync("text=Record update");
        await Page.FillAsync("#occurred", date);

        if (firmware is not null) await SelectByPrefixAsync("#fw", firmware);
        if (bootloader is not null) await SelectByPrefixAsync("#bl", bootloader);
        if (note.Length > 0) await Page.FillAsync("#note", note);

        await Page.ClickAsync("button[type=submit]:has-text('Record')");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
        return Page.Url;
    }

    /// <summary>
    /// The solution root, so the images land in docs/screenshots.
    /// </summary>
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MeshMuster.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the solution root.");
    }

    /// <summary>
    /// The width every shot is taken at.
    /// </summary>
    private const int Width = 1280;

    /// <summary>
    /// The height a page is first loaded at, before it is measured and reloaded.
    /// </summary>
    private const int ProbeHeight = 900;

    /// <summary>
    /// Add a board and its per-source asset patterns.
    /// </summary>
    /// <param name="board"></param>
    private async Task AddBoardAsync(DemoBoard board)
    {
        await Page.GotoAsync("/Boards");
        await Page.FillAsync("#board-name", board.Name);
        await Page.FillAsync("#board-description", board.Description);
        await Page.ClickAsync("button:has-text('Add board')");

        var card = Page.Locator($"[data-board='{board.Name}']");
        await Expect(card).ToBeVisibleAsync();

        foreach (var (source, pattern) in board.Patterns)
        {
            var row = card.Locator("tbody tr").Filter(
                new() { Has = Page.Locator($"td.card-title:text-is('{source}')") });
            await row.Locator("input[name=pattern]").FillAsync(pattern);
            await row.Locator("button.pattern-save").ClickAsync();
            await Expect(card).ToBeVisibleAsync();
        }
    }

    /// <summary>
    /// Add a device through the form, secrets and all.
    /// </summary>
    /// <param name="device"></param>
    private async Task AddDeviceAsync(DemoDevice device)
    {
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", device.Name);
        await Page.SelectOptionAsync("#Role", device.Role);
        await Page.SelectOptionAsync("#BoardId", new SelectOptionValue { Label = device.Board });
        await Page.SelectOptionAsync(
            "#FirmwareSourceId", new SelectOptionValue { Label = device.FirmwareSource });

        if (device.BootloaderSource.Length > 0)
        {
            await Page.SelectOptionAsync(
                "#BootloaderSourceId", new SelectOptionValue { Label = device.BootloaderSource });
        }

        if (device.PrivateKey.Length > 0) await Page.FillAsync("#PrivateKey", device.PrivateKey);
        if (device.AdminPassword.Length > 0) await Page.FillAsync("#AdminPassword", device.AdminPassword);
        if (device.Notes.Length > 0) await Page.FillAsync("#Notes", device.Notes);

        if (device.Latitude is not null && device.Longitude is not null)
        {
            await Page.FillAsync("#Latitude", device.Latitude.Value.ToString(CultureInfo.InvariantCulture));
            await Page.FillAsync("#Longitude", device.Longitude.Value.ToString(CultureInfo.InvariantCulture));
        }

        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
    }

    /// <summary>
    /// Record a flash whose firmware matches no release, so it reads as unknown.
    /// </summary>
    /// <param name="device"></param>
    /// <param name="date"></param>
    /// <param name="firmware"></param>
    /// <param name="bootloader"></param>
    private async Task UnknownFlashAsync(
        string device, string date, string firmware, string bootloader)
    {
        await Page.GotoAsync("/");
        await Page.ClickAsync($"a:has-text('{device}')");
        await Expect(Page.Locator("h1")).ToHaveTextAsync(device);

        await Page.ClickAsync("text=Record update");
        await Page.FillAsync("#occurred", date);
        await Page.SelectOptionAsync("#fw", new SelectOptionValue { Label = "not listed / unknown" });
        await Page.FillAsync("input[name=FirmwareLabel]", firmware);
        await SelectByPrefixAsync("#bl", bootloader);

        await Page.ClickAsync("button[type=submit]:has-text('Record')");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
    }

    /// <summary>A board in the demo dataset.</summary>
    /// <param name="Name"></param>
    /// <param name="Description"></param>
    /// <param name="Patterns"></param>
    private record DemoBoard(
        string Name, string Description, (string Source, string Pattern)[] Patterns);

    /// <summary>A device in the demo dataset.</summary>
    /// <param name="Name"></param>
    /// <param name="Role"></param>
    /// <param name="Board"></param>
    /// <param name="FirmwareSource"></param>
    /// <param name="BootloaderSource"></param>
    /// <param name="PrivateKey"></param>
    /// <param name="AdminPassword"></param>
    /// <param name="Notes"></param>
    /// <param name="Latitude"></param>
    /// <param name="Longitude"></param>
    private record DemoDevice(
        string Name, string Role, string Board, string FirmwareSource, string BootloaderSource,
        string PrivateKey, string AdminPassword, string Notes, double? Latitude, double? Longitude);

    /// <summary>
    /// The boards the shots are taken of.
    /// </summary>
    private static readonly DemoBoard[] Boards =
    [
        new("RAK4631", "WisBlock core, 1W booster",
        [
            ("MeshCore (official)", @"rak4631.*\.uf2$"),
            ("OTAFIX (oltaco)", @"rak4631.*\.zip$"),
        ]),
        new("Xiao S3 WIO", "Seeed Xiao ESP32-S3 + WIO SX1262",
        [
            ("MeshCore (official)", @"xiao.*\.uf2$"),
            ("MeshCore (mikecarper)", @"xiao.*\.uf2$"),
            ("OTAFIX (mikecarper)", @"xiao.*\.zip$"),
        ]),
    ];

    /// <summary>
    /// The devices the shots are taken of. Every key here is invented.
    /// </summary>
    private static readonly DemoDevice[] Devices =
    [
        new("Capitol Hill", "repeater", "RAK4631", "MeshCore (official)", "OTAFIX (oltaco)",
            "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279"
            + "896EC7ED326E0237BAB3A7B12BE04D07E33676B81670C4ABB822E6713BA8F3C8",
            "correct-horse-battery",
            "Rooftop, north face. Building key is with the super.", 47.6205, -122.3212),
        new("Alki Point", "repeater", "RAK4631", "MeshCore (official)", "OTAFIX (oltaco)",
            "D0C3BA7281628723C5C8F48F939830A6B1F534E3C2764E9DE3828E1824E56770"
            + "CF53535013BB557F94E6ABFFB1FEFE7438932BB25185D9C8FB21407C7BC823C2",
            "hunter2-but-longer", "Solar, west facing.", 47.5763, -122.4098),
        new("Gas Works", "repeater", "Xiao S3 WIO", "MeshCore (mikecarper)", "OTAFIX (mikecarper)",
            "80E2C120E99456EE58799E51266D26334ABEAE19733E9EF42647EC6ED07CF242"
            + "7FD23AF38474050193083BC3BE4D9FA39FEBFDEEFC2A62882DF3476CF23DF076",
            "", "", 47.6456, -122.3344),
        new("Bainbridge Ferry", "repeater", "Xiao S3 WIO", "MeshCore (official)", "",
            "", "", "Borrowed mast. Ask first.", 47.6235, -122.5108),
        new("Tacoma Dome", "repeater", "RAK4631", "MeshCore (official)", "OTAFIX (oltaco)",
            "", "", "Version is a guess; flashed before I started tracking.", 47.2364, -122.4267),
        new("Pocket companion", "companion", "Xiao S3 WIO", "MeshCore (official)", "",
            "", "", "", null, null),
        new("Truck", "companion", "Xiao S3 WIO", "MeshCore (mikecarper)", "OTAFIX (mikecarper)",
            "", "", "Lives in the glovebox.", null, null),
    ];
}
