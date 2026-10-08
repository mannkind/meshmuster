using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace MeshMuster.E2E;

[Parallelizable(ParallelScope.None)]
/// <summary>
/// Adding a device, recording a flash, and what the list says afterwards.
/// </summary>
public class DeviceTests : TestBase
{
    [Test]
    public async Task Health_endpoint_answers()
    {
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/health");
        Assert.That(response.Ok, Is.True);
    }

    [Test]
    public async Task Sources_page_lists_all_four_repositories_with_cached_releases()
    {
        await Page.GotoAsync("/Sources");
        var body = await Page.InnerTextAsync("body");

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("MeshCore (official)"));
            Assert.That(body, Does.Contain("MeshCore (mikecarper)"));
            Assert.That(body, Does.Contain("OTAFIX (oltaco)"));
            Assert.That(body, Does.Contain("OTAFIX (mikecarper)"));
            Assert.That(body, Does.Contain("1.17.1"));
            Assert.That(body, Does.Contain("OTAFIX 2.3"));
        });
    }

    [Test]
    public async Task A_device_on_the_newest_release_reads_as_current()
    {
        var url = await AddDeviceAsync(
            "e2e-current", "repeater", "MeshCore (official)", "OTAFIX (oltaco)");

        await RecordFlashAsync("1.17.1", "OTAFIX 2.3");

        await Page.GotoAsync(url);
        var body = await Page.InnerTextAsync("body");

        Assert.That(body, Does.Contain("current"));
        Assert.That(body, Does.Not.Contain("↑"));
    }

    [Test]
    public async Task A_device_behind_its_channel_shows_the_target_version()
    {
        var url = await AddDeviceAsync(
            "e2e-behind", "repeater", "MeshCore (official)", "OTAFIX (oltaco)");

        await RecordFlashAsync("1.16.0", null);

        await Page.GotoAsync(url);
        var firmware = await Page.InnerTextAsync("dl.detail");

        Assert.That(firmware, Does.Contain("1.17.1"));
    }

    [Test]
    public async Task Recording_a_flash_writes_history_and_clears_the_badge()
    {
        var url = await AddDeviceAsync(
            "e2e-flash", "repeater", "MeshCore (official)", "OTAFIX (oltaco)");

        await RecordFlashAsync("1.16.0", null);
        await Page.GotoAsync(url);
        Assert.That(await Page.InnerTextAsync("body"), Does.Contain("1.16.0"));

        await RecordFlashAsync("1.17.1", null);
        await Page.GotoAsync(url);

        var body = await Page.InnerTextAsync("body");
        Assert.Multiple(() =>
        {
            // The history keeps both, with an arrow showing the move.
            Assert.That(body, Does.Contain("1.16.0"));
            Assert.That(body, Does.Contain("1.17.1"));
            Assert.That(body, Does.Contain("current"));
        });
    }

    [Test]
    public async Task An_unknown_version_never_claims_to_be_current()
    {
        var url = await AddDeviceAsync("e2e-unknown", "repeater", "MeshCore (official)");

        await Page.ClickAsync("text=Record update");
        await Page.SelectOptionAsync("#fw", new SelectOptionValue { Label = "not listed / unknown" });
        await Page.FillAsync("input[name=FirmwareLabel]", "1.09.x");
        await Page.ClickAsync("button[type=submit]:has-text('Record')");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        await Page.GotoAsync(url);
        var body = await Page.InnerTextAsync("body");

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("1.09.x"));
            Assert.That(body, Does.Contain("unknown"));
            Assert.That(body, Does.Not.Contain("current"));
        });
    }

    [Test]
    public async Task Changing_the_role_moves_the_device_onto_the_companion_stream()
    {
        var url = await AddDeviceAsync("e2e-role", "repeater", "MeshCore (official)");

        await Page.GotoAsync(url.Replace("/DeviceDetail/", "/DeviceEdit/"));
        await Page.SelectOptionAsync("#Role", "companion");
        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        await Page.ClickAsync("text=Record update");
        var options = await Page.Locator("#fw option").AllInnerTextsAsync();

        Assert.That(options.Any(o => o.Contains("1.17.1")), Is.True);
    }

    [Test]
    public async Task First_firmware_and_bootloader_options_remain_selected_after_save()
    {
        var detailUrl = await AddDeviceAsync("e2e-first-options", "repeater",
            "MeshCore (mikecarper)", "OTAFIX (mikecarper)");
        await Page.GotoAsync(detailUrl.Replace("/DeviceDetail/", "/DeviceEdit/"));

        var firmware = await Page.Locator("#FirmwareSourceId option").Nth(1).GetAttributeAsync("value");
        var bootloader = await Page.Locator("#BootloaderSourceId option").Nth(1).GetAttributeAsync("value");
        await Page.SelectOptionAsync("#FirmwareSourceId", firmware!);
        await Page.SelectOptionAsync("#BootloaderSourceId", bootloader!);
        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        await Page.GotoAsync(detailUrl.Replace("/DeviceDetail/", "/DeviceEdit/"));
        Assert.That(await Page.Locator("#FirmwareSourceId").InputValueAsync(), Is.EqualTo(firmware));
        Assert.That(await Page.Locator("#BootloaderSourceId").InputValueAsync(), Is.EqualTo(bootloader));

        await Page.GotoAsync(detailUrl);
        await Page.ClickAsync("text=Record update");
        var firmwareRelease = await Page.Locator("#fw option").Nth(1).GetAttributeAsync("value");
        var bootloaderRelease = await Page.Locator("#bl option").Nth(1).GetAttributeAsync("value");
        await Page.SelectOptionAsync("#fw", firmwareRelease!);
        await Page.SelectOptionAsync("#bl", bootloaderRelease!);
        await Page.ClickAsync("button[type=submit]:has-text('Record')");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        var detail = await Page.InnerTextAsync("dl.detail");
        Assert.That(detail, Does.Not.Contain("no channel set"));
        Assert.That(detail, Does.Contain("1.17.1 current"));
        Assert.That(detail, Does.Contain("OTAFIX 2.3 current"));
    }

    [Test]
    public async Task Kiss_node_first_channel_choices_do_not_silently_disappear()
    {
        var detailUrl = await AddDeviceAsync("e2e-kiss-first-options", "kiss",
            "MeshCore (mikecarper)", "OTAFIX (mikecarper)");
        await Page.GotoAsync(detailUrl.Replace("/DeviceDetail/", "/DeviceEdit/"));

        var firmware = await Page.Locator("#FirmwareSourceId option").Nth(1).GetAttributeAsync("value");
        var bootloader = await Page.Locator("#BootloaderSourceId option").Nth(1).GetAttributeAsync("value");
        await Page.SelectOptionAsync("#FirmwareSourceId", firmware!);
        await Page.SelectOptionAsync("#BootloaderSourceId", bootloader!);
        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        var detail = await Page.InnerTextAsync("dl.detail");
        Assert.That(detail, Does.Contain("MeshCore (official)"));
        Assert.That(detail, Does.Contain("OTAFIX (oltaco)"));

        await Page.GotoAsync(detailUrl.Replace("/DeviceDetail/", "/DeviceEdit/"));
        Assert.That(await Page.Locator("#FirmwareSourceId").InputValueAsync(), Is.EqualTo(firmware));
        Assert.That(await Page.Locator("#BootloaderSourceId").InputValueAsync(), Is.EqualTo(bootloader));
    }

    [Test]
    public async Task Secrets_are_masked_on_the_detail_page_and_absent_from_the_list()
    {
        const string privateKey =
            "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279" +
            "896EC7ED326E0237BAB3A7B12BE04D07E33676B81670C4ABB822E6713BA8F3C8";
        const string adminPassword = "admin's <secret> & \"password\"";

        await Page.RouteAsync("https://cdn.jsdelivr.net/**", route => route.AbortAsync());
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-secrets");
        await Page.FillAsync("#PrivateKey", privateKey);
        await Page.FillAsync("#AdminPassword", adminPassword);
        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        // The public key is derived, while both stored values remain masked until revealed.
        var visible = await Page.InnerTextAsync("dl.detail");
        Assert.That(visible, Does.Contain("ABC1225608250503E7B73C49AA384635FCDDE1CD455922FFD46D486CA27D5923"));
        Assert.That(visible, Does.Not.Contain(privateKey));
        Assert.That(visible, Does.Not.Contain(adminPassword));

        await Page.Locator(".secret-disclosure summary").Nth(0).ClickAsync();
        await Page.Locator(".secret-disclosure summary").Nth(1).ClickAsync();
        visible = await Page.InnerTextAsync("dl.detail");
        Assert.That(visible, Does.Contain(privateKey));
        Assert.That(visible, Does.Contain(adminPassword));

        await Page.Locator(".secret-disclosure summary").Nth(0).ClickAsync();
        Assert.That(await Page.InnerTextAsync("dl.detail"), Does.Not.Contain(privateKey));

        // And never written into the list at all.
        await Page.GotoAsync("/");
        Assert.That(await Page.ContentAsync(), Does.Not.Contain(privateKey));
        Assert.That(await Page.ContentAsync(), Does.Not.Contain(adminPassword));
    }

    [Test]
    public async Task Kiss_private_key_can_be_revealed_without_browser_scripts()
    {
        const string privateKey =
            "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279" +
            "896EC7ED326E0237BAB3A7B12BE04D07E33676B81670C4ABB822E6713BA8F3C8";

        await Page.RouteAsync("https://cdn.jsdelivr.net/**", route => route.AbortAsync());
        await Page.RouteAsync("**/app.js*", route => route.AbortAsync());
        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-kiss-key");
        await Page.SelectOptionAsync("#Role", "kiss");
        await Page.FillAsync("#PrivateKey", privateKey);
        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        Assert.That(await Page.InnerTextAsync("dl.detail"), Does.Contain("ABC1225608250503"));
        Assert.That(await Page.InnerTextAsync("dl.detail"), Does.Not.Contain(privateKey));
        await Page.Locator(".secret-disclosure summary").ClickAsync();
        Assert.That(await Page.InnerTextAsync("dl.detail"), Does.Contain(privateKey));
    }

    [Test]
    public async Task Editing_a_kiss_node_keeps_its_existing_secrets()
    {
        const string privateKey =
            "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279" +
            "896EC7ED326E0237BAB3A7B12BE04D07E33676B81670C4ABB822E6713BA8F3C8";
        const string adminPassword = "admin's <secret> & \"password\"";

        await Page.GotoAsync("/DeviceEdit");
        await Page.FillAsync("#Name", "e2e-kiss-edit-secrets");
        await Page.SelectOptionAsync("#Role", "kiss");
        await Page.FillAsync("#PrivateKey", privateKey);
        await Page.FillAsync("#AdminPassword", adminPassword);
        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        var detailUrl = Page.Url;
        await Page.GotoAsync(detailUrl.Replace("/DeviceDetail/", "/DeviceEdit/"));
        Assert.That(await Page.Locator("#PrivateKey").InputValueAsync(), Is.EqualTo(privateKey));
        Assert.That(await Page.Locator("#AdminPassword").InputValueAsync(), Is.EqualTo(adminPassword));

        await Page.ClickAsync("button[type=submit]");
        await Expect(Page).ToHaveURLAsync(DetailUrl);
        Assert.That(await Page.InnerTextAsync("dl.detail"), Does.Contain("ABC1225608250503"));
    }

    [Test]
    public async Task Kiss_node_can_record_a_synced_official_version()
    {
        await AddDeviceAsync("e2e-kiss-manual-version", "kiss", "MeshCore (official)");
        await Page.ClickAsync("text=Record update");
        await Page.SelectOptionAsync("#fw", new SelectOptionValue { Label = "1.17.1" });
        await Page.ClickAsync("button[type=submit]:has-text('Record')");
        await Expect(Page).ToHaveURLAsync(DetailUrl);

        var detail = await Page.InnerTextAsync("dl.detail");
        Assert.That(detail, Does.Contain("1.17.1 current"));
        Assert.That(await Page.InnerTextAsync("body"), Does.Contain("1.17.1"));
    }

    [Test]
    public async Task Sorting_by_version_is_numeric_not_lexical()
    {
        await AddDeviceAsync("e2e-sort-a", "repeater", "MeshCore (official)");
        await RecordFlashAsync("1.16.0", null);

        await AddDeviceAsync("e2e-sort-b", "repeater", "MeshCore (official)");
        await RecordFlashAsync("1.17.1", null);

        await Page.GotoAsync("/?sort=firmware&desc=true");
        var names = await Page.Locator("a.device-name").AllInnerTextsAsync();

        var a = names.ToList().IndexOf("e2e-sort-a");
        var b = names.ToList().IndexOf("e2e-sort-b");

        // As plain strings these sort the wrong way round.
        Assert.That(b, Is.LessThan(a));
    }

    [Test]
    public async Task Boards_page_previews_which_assets_a_pattern_matches()
    {
        await Page.GotoAsync("/Boards");
        await Page.FillAsync("#board-name", "e2e-board");
        await Page.ClickAsync("button:has-text('Add board')");

        var card = Page.Locator(".panel[data-board='e2e-board']");
        await card.Locator("input[name=pattern]").First.FillAsync(@"rak4631.*\.uf2$");
        await card.Locator("button.pattern-save").First.ClickAsync();

        await Page.GotoAsync("/Boards");
        card = Page.Locator(".panel[data-board='e2e-board']");
        await card.Locator("button:has-text('preview matches')").ClickAsync();

        await Expect(card.Locator("text=rak4631_repeater.uf2").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_board_can_be_renamed_and_given_notes_after_it_exists()
    {
        await Page.GotoAsync("/Boards");
        await Page.FillAsync("#board-name", "e2e-board-edit");
        await Page.ClickAsync("button:has-text('Add board')");

        var card = Page.Locator(".panel[data-board='e2e-board-edit']");

        // Reads as text until you click it.
        await Expect(card.Locator(".board-fields")).ToBeHiddenAsync();
        await card.Locator(".board-view").ClickAsync();
        await Expect(card.Locator(".board-fields")).ToBeVisibleAsync();

        await card.Locator("input[name=description]").FillAsync("Use the Xiao nRF build");
        await card.Locator("input[name=name]").FillAsync("e2e-board-renamed");
        await card.Locator("button.board-save").ClickAsync();

        await Page.GotoAsync("/Boards");
        var renamed = Page.Locator(".panel[data-board='e2e-board-renamed']");
        await Expect(renamed.Locator(".board-view")).ToContainTextAsync("e2e-board-renamed");
        await Expect(renamed.Locator(".board-view")).ToContainTextAsync("Use the Xiao nRF build");
    }

    [Test]
    public async Task Cancelling_an_edit_leaves_the_board_alone()
    {
        await Page.GotoAsync("/Boards");
        await Page.FillAsync("#board-name", "e2e-board-cancel");
        await Page.ClickAsync("button:has-text('Add board')");

        var card = Page.Locator(".panel[data-board='e2e-board-cancel']");
        await card.Locator(".board-view").ClickAsync();
        await card.Locator("input[name=name]").FillAsync("typed-but-abandoned");
        await card.Locator("button.board-cancel").ClickAsync();

        await Expect(card.Locator(".board-fields")).ToBeHiddenAsync();
        // Re-opening should show the stored name, not what was abandoned.
        await card.Locator(".board-view").ClickAsync();
        Assert.That(await card.Locator("input[name=name]").InputValueAsync(),
            Is.EqualTo("e2e-board-cancel"));

        await Page.GotoAsync("/Boards");
        await Expect(Page.Locator(".panel[data-board='e2e-board-cancel']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Renaming_a_board_onto_an_existing_name_is_refused()
    {
        await Page.GotoAsync("/Boards");
        await Page.FillAsync("#board-name", "e2e-board-first");
        await Page.ClickAsync("button:has-text('Add board')");
        await Page.FillAsync("#board-name", "e2e-board-second");
        await Page.ClickAsync("button:has-text('Add board')");

        var card = Page.Locator(".panel[data-board='e2e-board-second']");
        await card.Locator(".board-view").ClickAsync();
        await card.Locator("input[name=name]").FillAsync("e2e-board-first");
        await card.Locator("button.board-save").ClickAsync();

        await Expect(Page.Locator(".flash")).ToContainTextAsync("already a board");
    }
}
