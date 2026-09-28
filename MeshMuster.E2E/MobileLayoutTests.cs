using Microsoft.Playwright;

namespace MeshMuster.E2E;

[Parallelizable(ParallelScope.None)]
/// <summary>
/// The phone-width layout: nothing scrolls sideways, and the card controls still work.
/// </summary>
public class MobileLayoutTests : TestBase
{
    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = Harness.App.BaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 393, Height = 852 },
        };
    [Test]
    public async Task Every_page_fits_a_phone_without_scrolling_sideways()
    {
        var detailUrl = await AddDeviceAsync(
            "e2e-mobile-node", "repeater", "MeshCore (official)", "OTAFIX (oltaco)",
            47.6062, -122.3321);
        await RecordFlashAsync("1.16.0", "OTAFIX 2.2");

        await Page.GotoAsync("/Boards");
        await Page.FillAsync("#board-name", "e2e-mobile-board");
        await Page.ClickAsync("button:has-text('Add board')");

        string[] paths =
        [
            "/", "/Map", "/Boards", "/Sources", "/DeviceEdit",
            detailUrl, detailUrl.Replace("/DeviceDetail/", "/DeviceEdit/"),
        ];

        foreach (var path in paths)
        {
            await Page.GotoAsync(path);
            var offenders = await Page.EvaluateAsync<string[]>(SidewaysScrollers);

            Assert.That(offenders, Is.Empty,
                $"{path} scrolls sideways at 393px: {string.Join(", ", offenders)}");
        }
    }

    [Test]
    public async Task Devices_become_cards_with_a_label_on_every_field()
    {
        await AddDeviceAsync("e2e-mobile-cards", "repeater", "MeshCore (official)");
        await Page.GotoAsync("/");

        await Expect(Page.Locator("table.cards thead").First).ToBeHiddenAsync();

        var card = Page.Locator("table.cards tbody tr:has-text('e2e-mobile-cards')").First;
        foreach (var label in new[] { "Key", "Board", "Firmware", "Bootloader", "Updated" })
        {
            await Expect(card.Locator($"td[data-label='{label}']")).ToHaveCountAsync(1);
        }
    }

    [Test]
    public async Task The_card_layout_can_still_be_sorted()
    {
        await AddDeviceAsync("e2e-mobile-sort-a", "repeater", "MeshCore (official)");
        await RecordFlashAsync("1.16.0", null);
        await AddDeviceAsync("e2e-mobile-sort-b", "repeater", "MeshCore (official)");
        await RecordFlashAsync("1.17.1", null);

        await Page.GotoAsync("/");

        await Expect(Page.Locator(".card-sort")).ToBeVisibleAsync();
        await Page.SelectOptionAsync("#sort-by", "firmware");
        await Page.WaitForURLAsync("**/*sort=firmware*");

        var ascending = (await Page.Locator("a.device-name").AllInnerTextsAsync()).ToList();
        Assert.That(ascending.IndexOf("e2e-mobile-sort-a"),
            Is.LessThan(ascending.IndexOf("e2e-mobile-sort-b")), "1.16.0 sorts below 1.17.1.");

        await Page.Locator(".card-sort button[name=desc]").ClickAsync();
        await Page.WaitForURLAsync("**/*desc=true*");

        var descending = (await Page.Locator("a.device-name").AllInnerTextsAsync()).ToList();
        Assert.That(descending.IndexOf("e2e-mobile-sort-b"),
            Is.LessThan(descending.IndexOf("e2e-mobile-sort-a")),
            "Flipping the direction puts 1.17.1 first.");
    }

    /// <summary>
    /// Reports anything scrolling sideways; the map is exempt, it pans on purpose.
    /// </summary>
    private const string SidewaysScrollers = """
        () => {
            const out = [];
            const root = document.documentElement;
            const slack = 1;

            if (root.scrollWidth - root.clientWidth > slack)
                out.push('page by ' + (root.scrollWidth - root.clientWidth) + 'px');

            for (const el of document.querySelectorAll('*')) {
                if (el.closest('.leaflet-container')) continue;
                if (el.scrollWidth - el.clientWidth <= slack) continue;

                const overflowX = getComputedStyle(el).overflowX;
                if (overflowX !== 'auto' && overflowX !== 'scroll') continue;

                const cls = typeof el.className === 'string' && el.className.trim()
                    ? '.' + el.className.trim().split(/\s+/).join('.')
                    : '';
                out.push(el.tagName.toLowerCase() + cls +
                    ' by ' + (el.scrollWidth - el.clientWidth) + 'px');
            }
            return out;
        }
        """;
}
