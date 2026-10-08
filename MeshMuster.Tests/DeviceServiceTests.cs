using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using MeshMuster.Services.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MeshMuster.Tests;

/// <summary>
/// Creating devices, moving them between streams, and recording flashes.
/// </summary>
public class DeviceServiceTests
{

    [SetUp]
    public async Task SetUp()
    {
        this.Db = new SqliteTestDatabase();
        await this.Db.MigrateAsync();
        this.Clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown() => this.Db.Dispose();

    [Test]
    public async Task Resolves_the_stream_from_source_and_role()
    {
        var id = await CreateRepeaterAsync();

        await using var context = this.Db.NewContext();
        var device = await context.Devices
            .Include(d => d.FirmwareStream)
            .FirstAsync(d => d.Id == id);

        Assert.That(device.FirmwareStream!.Slug, Is.EqualTo("repeater"));
    }

    [TestCase("meshcore-official", DeviceRole.Room, "room")]
    [TestCase("meshcore-mikecarper", DeviceRole.Room, "room")]
    [TestCase("meshcore-mikecarper", DeviceRole.Sensor, "sensor")]
    [TestCase("meshcore-mikecarper", DeviceRole.Kiss, "kiss")]
    [TestCase("meshcore-official", DeviceRole.Sensor, "sensor")]
    [TestCase("meshcore-official", DeviceRole.Kiss, "kiss")]
    public async Task Resolves_the_stream_for_the_newer_roles(
        string sourceSlug, string role, string expectedSlug)
    {
        Guid id;
        await using (var context = this.Db.NewContext())
        {
            var sourceId = (await context.Sources.FirstAsync(s => s.Slug == sourceSlug)).Id;
            var device = await Service(context).CreateAsync(new DeviceEdit
            {
                Name = $"{role} node",
                Role = role,
                BoardId = null,
                FirmwareSourceId = sourceId,
                BootloaderSourceId = null,
                PrivateKey = "",
                AdminPassword = "",
                Notes = "",
                IsActive = true,
            });
            id = device.Id;
        }

        await using var verify = this.Db.NewContext();
        var saved = await verify.Devices.Include(d => d.FirmwareStream).FirstAsync(d => d.Id == id);

        Assert.That(saved.FirmwareStream!.Slug, Is.EqualTo(expectedSlug));
    }

    [TestCase(DeviceRole.Sensor)]
    [TestCase(DeviceRole.Kiss)]
    public async Task The_official_repo_can_be_selected_without_sensor_or_kiss_releases(string role)
    {
        Guid id;
        await using (var context = this.Db.NewContext())
        {
            var sourceId = (await context.Sources
                .FirstAsync(s => s.Slug == "meshcore-official")).Id;
            var device = await Service(context).CreateAsync(new DeviceEdit
            {
                Name = $"{role} node",
                Role = role,
                BoardId = null,
                FirmwareSourceId = sourceId,
                BootloaderSourceId = null,
                PrivateKey = "",
                AdminPassword = "",
                Notes = "",
                IsActive = true,
            });
            id = device.Id;
        }

        await using var verify = this.Db.NewContext();
        var saved = await verify.Devices.Include(d => d.FirmwareStream).FirstAsync(d => d.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(saved.FirmwareStream!.Slug, Is.EqualTo(role));
            Assert.That(verify.Releases.Count(r => r.StreamId == saved.FirmwareStreamId), Is.Zero);
        });
    }

    [Test]
    public async Task Changing_the_role_moves_the_device_onto_the_matching_stream()
    {
        var id = await CreateRepeaterAsync();

        await using (var context = this.Db.NewContext())
        {
            var sourceId = (await context.Sources.FirstAsync(s => s.Slug == "meshcore-official")).Id;
            await Service(context).UpdateAsync(id, new DeviceEdit
            {
                Name = "The Brewery",
                Role = DeviceRole.Companion,
                BoardId = null,
                FirmwareSourceId = sourceId,
                BootloaderSourceId = null,
                PrivateKey = "key",
                AdminPassword = "pw",
                Notes = "",
                IsActive = true,
            });
        }

        await using var verify = this.Db.NewContext();
        var device = await verify.Devices.Include(d => d.FirmwareStream).FirstAsync(d => d.Id == id);

        Assert.That(device.FirmwareStream!.Slug, Is.EqualTo("companion"));
    }

    [Test]
    public async Task A_bootloader_source_resolves_to_its_single_role_agnostic_stream()
    {
        var id = await CreateRepeaterAsync();

        await using var context = this.Db.NewContext();
        var device = await context.Devices
            .Include(d => d.BootloaderStream).ThenInclude(s => s!.Source)
            .FirstAsync(d => d.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(device.BootloaderStream!.Slug, Is.EqualTo("default"));
            Assert.That(device.BootloaderStream.Source!.Slug, Is.EqualTo("otafix-oltaco"));
        });
    }

    [Test]
    public async Task Recording_a_flash_writes_the_log_and_the_current_version_together()
    {
        await SeedReleasesAsync();
        var id = await CreateRepeaterAsync();

        Guid releaseId;
        await using (var context = this.Db.NewContext())
            releaseId = (await context.Releases.FirstAsync(r => r.Tag == "repeater-v1.17.1")).Id;

        await using (var context = this.Db.NewContext())
        {
            await Service(context).RecordFlashAsync(id, new FlashRecord
            {
                OccurredAt = new DateOnly(2026, 9, 5),
                Firmware = new VersionChoice
                {
                    ReleaseId = releaseId,
                    Label = "",
                },
                Bootloader = VersionChoice.Unchanged,
                Note = "bench flash",
            });
        }

        await using var verify = this.Db.NewContext();
        var device = await verify.Devices.FirstAsync(d => d.Id == id);
        var flash = await verify.DeviceFlashes.SingleAsync(f => f.DeviceId == id);

        Assert.Multiple(() =>
        {
            Assert.That(device.FirmwareVersionLabel, Is.EqualTo("1.17.1"));
            Assert.That(device.FirmwareReleaseId, Is.EqualTo(releaseId));
            Assert.That(device.FirmwareSortKey, Is.EqualTo("00001.00017.00001.00000"));
            Assert.That(flash.FirmwareToLabel, Is.EqualTo("1.17.1"));
            Assert.That(flash.FirmwareFromLabel, Is.Empty);
            Assert.That(flash.OccurredAt, Is.EqualTo(new DateOnly(2026, 9, 5)));
            Assert.That(flash.Note, Is.EqualTo("bench flash"));
        });
    }

    [Test]
    public async Task Recording_the_same_version_again_writes_nothing()
    {
        await SeedReleasesAsync();
        var id = await CreateRepeaterAsync();

        Guid releaseId;
        await using (var context = this.Db.NewContext())
            releaseId = (await context.Releases.FirstAsync(r => r.Tag == "repeater-v1.17.1")).Id;

        for (var i = 0; i < 2; i++)
        {
            await using var context = this.Db.NewContext();
            await Service(context).RecordFlashAsync(id, new FlashRecord
            {
                OccurredAt = new DateOnly(2026, 9, 5),
                Firmware = new VersionChoice
                {
                    ReleaseId = releaseId,
                    Label = "",
                },
                Bootloader = VersionChoice.Unchanged,
                Note = "",
            });
        }

        // A second identical flash would put a false date on "last updated".
        Assert.That(await this.Db.Count("device_flashes"), Is.EqualTo(1));
    }

    [Test]
    public async Task A_not_listed_version_keeps_its_label_and_sorts_as_unknown()
    {
        var id = await CreateRepeaterAsync();

        await using (var context = this.Db.NewContext())
        {
            await Service(context).RecordFlashAsync(id, new FlashRecord
            {
                OccurredAt = new DateOnly(2026, 1, 1),
                Firmware = new VersionChoice
                {
                    ReleaseId = null,
                    Label = "1.09.x",
                },
                Bootloader = VersionChoice.Unchanged,
                Note = "",
            });
        }

        await using var verify = this.Db.NewContext();
        var device = await verify.Devices.FirstAsync(d => d.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(device.FirmwareVersionLabel, Is.EqualTo("1.09.x"));
            Assert.That(device.FirmwareReleaseId, Is.Null);
            Assert.That(device.FirmwareSortKey, Is.Empty);
        });
    }

    [Test]
    public async Task Editing_a_device_does_not_write_a_flash_row()
    {
        var id = await CreateRepeaterAsync();

        await using (var context = this.Db.NewContext())
        {
            var sourceId = (await context.Sources.FirstAsync(s => s.Slug == "meshcore-official")).Id;
            await Service(context).UpdateAsync(id, new DeviceEdit
            {
                Name = "Renamed",
                Role = DeviceRole.Repeater,
                BoardId = null,
                FirmwareSourceId = sourceId,
                BootloaderSourceId = null,
                PrivateKey = "new-key",
                AdminPassword = "new-password",
                Notes = "notes",
                IsActive = true,
            });
        }

        // Rotating a password isn't a flash, so it shouldn't move "last updated".
        Assert.That(await this.Db.Count("device_flashes"), Is.Zero);
    }

    [Test]
    public async Task Last_updated_comes_from_the_newest_flash()
    {
        var id = await CreateRepeaterAsync();

        await using (var context = this.Db.NewContext())
        {
            var service = Service(context);
            await service.RecordFlashAsync(id, new FlashRecord
            {
                OccurredAt = new DateOnly(2026, 3, 1),
                Firmware = new VersionChoice
                {
                    ReleaseId = null,
                    Label = "1.16.x",
                },
                Bootloader = VersionChoice.Unchanged,
                Note = "",
            });
            await service.RecordFlashAsync(id, new FlashRecord
            {
                OccurredAt = new DateOnly(2026, 7, 15),
                Firmware = new VersionChoice
                {
                    ReleaseId = null,
                    Label = "1.17.1",
                },
                Bootloader = VersionChoice.Unchanged,
                Note = "",
            });
        }

        await using var verify = this.Db.NewContext();
        var lastFlashed = await Service(verify).LastFlashedAsync();

        Assert.That(lastFlashed[id], Is.EqualTo(new DateOnly(2026, 7, 15)));
    }

    [Test]
    public async Task Deleting_a_device_removes_its_flash_history()
    {
        var id = await CreateRepeaterAsync();

        await using (var context = this.Db.NewContext())
            await Service(context).RecordFlashAsync(id, new FlashRecord
            {
                OccurredAt = new DateOnly(2026, 3, 1),
                Firmware = new VersionChoice
                {
                    ReleaseId = null,
                    Label = "1.16.x",
                },
                Bootloader = VersionChoice.Unchanged,
                Note = "",
            });

        await using (var context = this.Db.NewContext())
            await Service(context).DeleteAsync(id);

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.Count("devices"), Is.Zero);
            Assert.That(await this.Db.Count("device_flashes"), Is.Zero);
        });
    }

    [Test]
    public async Task Sorting_by_firmware_uses_the_sort_key_not_the_label()
    {
        await using (var context = this.Db.NewContext())
        {
            var sourceId = (await context.Sources.FirstAsync(s => s.Slug == "meshcore-official")).Id;
            var service = Service(context);
            foreach (var (name, version) in new[] { ("b", "1.9.0"), ("a", "1.17.0") })
            {
                var device = await service.CreateAsync(new DeviceEdit
                {
                    Name = name,
                    Role = DeviceRole.Repeater,
                    BoardId = null,
                    FirmwareSourceId = sourceId,
                    BootloaderSourceId = null,
                    PrivateKey = "",
                    AdminPassword = "",
                    Notes = "",
                    IsActive = true,
                });
                device.FirmwareVersionLabel = version;
                device.FirmwareSortKey = VersionSortKey.FromDottedNumber(version);
                await context.SaveChangesAsync();
            }
        }

        await using var verify = this.Db.NewContext();
        var ordered = await Service(verify).ListAsync(SortColumn.Firmware, descending: false);

        Assert.That(ordered.Select(d => d.FirmwareVersionLabel), Is.EqualTo(new[] { "1.9.0", "1.17.0" }));
    }

    private SqliteTestDatabase Db = null!;
    private FakeTimeProvider Clock = null!;

    /// <summary>
    /// A device service over the given context.
    /// </summary>
    /// <param name="context"></param>
    private DeviceService Service(AppDbContext context) =>
        new(context, new StreamResolver(context), this.Clock);

    private async Task SeedReleasesAsync()
    {
        var client = new StubGitHubClient();
        client.Returns(
            StubGitHubClient.Release(1, "repeater-v1.16.0"),
            StubGitHubClient.Release(2, "repeater-v1.17.1"),
            StubGitHubClient.Release(3, "companion-v1.17.1"));

        await using var context = this.Db.NewContext();
        var sync = new ReleaseSyncService(
            context, client, new VersionSchemeRegistry(), new SyncGate(), this.Clock,
            NullLogger<ReleaseSyncService>.Instance);
        await sync.SyncAsync(await context.Sources.FirstAsync(s => s.Slug == "meshcore-official"));
    }

    /// <summary>
    /// A repeater wired to both sources.
    /// </summary>
    /// <param name="name"></param>
    private async Task<Guid> CreateRepeaterAsync(string name = "The Brewery")
    {
        await using var context = this.Db.NewContext();
        var sourceId = (await context.Sources.FirstAsync(s => s.Slug == "meshcore-official")).Id;
        var bootloaderId = (await context.Sources.FirstAsync(s => s.Slug == "otafix-oltaco")).Id;

        var device = await Service(context).CreateAsync(new DeviceEdit
        {
            Name = name,
            Role = DeviceRole.Repeater,
            BoardId = null,
            FirmwareSourceId = sourceId,
            BootloaderSourceId = bootloaderId,
            PrivateKey = "key",
            AdminPassword = "pw",
            Notes = "",
            IsActive = true,
        });

        return device.Id;
    }
}
