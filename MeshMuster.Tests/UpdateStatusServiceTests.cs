using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using MeshMuster.Services.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MeshMuster.Tests;

/// <summary>
/// Judging a device against its channel, over real synced releases.
/// </summary>
public class UpdateStatusServiceTests
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
    public async Task Reports_up_to_date_when_installed_matches_latest()
    {
        await SeedOfficialAsync((1, "repeater-v1.16.0"), (2, "repeater-v1.17.1"));

        await using var context = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(context);

        var status = service.Evaluate(
            snapshot, streamId, "1.17.1", VersionSortKey.FromDottedNumber("1.17.1"), null);

        Assert.That(status.State, Is.EqualTo(UpdateState.UpToDate));
    }

    [Test]
    public async Task Reports_an_update_with_a_real_count_of_releases_behind()
    {
        await SeedOfficialAsync(
            (1, "repeater-v1.15.0"), (2, "repeater-v1.16.0"), (3, "repeater-v1.17.1"));

        await using var context = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(context);

        var status = service.Evaluate(
            snapshot, streamId, "1.15.0", VersionSortKey.FromDottedNumber("1.15.0"), null);

        Assert.Multiple(() =>
        {
            Assert.That(status.State, Is.EqualTo(UpdateState.UpdateAvailable));
            Assert.That(status.Latest!.VersionLabel, Is.EqualTo("1.17.1"));
            Assert.That(status.ReleasesBehind, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task A_version_we_could_not_resolve_reads_as_unknown_not_current()
    {
        await SeedOfficialAsync((1, "repeater-v1.17.1"));

        await using var context = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(context);

        var status = service.Evaluate(snapshot, streamId, "1.09.x", "", null);

        Assert.Multiple(() =>
        {
            Assert.That(status.State, Is.EqualTo(UpdateState.Unknown));
            Assert.That(status.NeedsAttention, Is.True);
            Assert.That(status.Latest!.VersionLabel, Is.EqualTo("1.17.1"));
        });
    }

    [Test]
    public async Task No_channel_set_is_its_own_state()
    {
        await using var context = this.Db.NewContext();
        var service = new UpdateStatusService(context);
        var snapshot = await service.LoadSnapshotAsync();

        var status = service.Evaluate(snapshot, null, "1.17.1", "x", null);

        Assert.That(status.State, Is.EqualTo(UpdateState.NoChannelSet));
    }

    [Test]
    public async Task A_stale_source_never_claims_the_device_is_current()
    {
        await SeedOfficialAsync((1, "repeater-v1.17.1"));

        await using (var context = this.Db.NewContext())
        {
            var source = await context.Sources.FirstAsync(s => s.Slug == "meshcore-official");
            source.LastPollError = "connection refused";
            await context.SaveChangesAsync();
        }

        await using var verify = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(verify);

        var status = service.Evaluate(
            snapshot, streamId, "1.17.1", VersionSortKey.FromDottedNumber("1.17.1"), null);

        Assert.Multiple(() =>
        {
            Assert.That(status.State, Is.EqualTo(UpdateState.SourceStale));
            Assert.That(status.StaleReason, Does.Contain("connection refused"));
        });
    }

    [Test]
    public async Task Prereleases_are_excluded_when_the_source_says_so()
    {
        var client = new StubGitHubClient();
        client.Returns(
            StubGitHubClient.Release(1, "repeater-v1.16.0"),
            StubGitHubClient.Release(2, "repeater-v1.17.1", prerelease: true));

        await using (var context = this.Db.NewContext())
        {
            var sync = new ReleaseSyncService(
                context, client, new VersionSchemeRegistry(), new SyncGate(), this.Clock,
                NullLogger<ReleaseSyncService>.Instance);
            await sync.SyncAsync(await context.Sources.FirstAsync(s => s.Slug == "meshcore-official"));
        }

        await using var verify = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(verify);

        var status = service.Evaluate(
            snapshot, streamId, "1.16.0", VersionSortKey.FromDottedNumber("1.16.0"), null);

        // Official ignores prereleases, so 1.17.1 shouldn't count as an update.
        Assert.That(status.State, Is.EqualTo(UpdateState.UpToDate));
    }

    [Test]
    public async Task Matches_a_download_asset_for_the_board()
    {
        await SeedOfficialAsync((1, "repeater-v1.16.0"), (2, "repeater-v1.17.1"));

        Guid boardId;
        await using (var context = this.Db.NewContext())
        {
            var board = new Board { Id = Guid.NewGuid(), Name = "Xiao", CreatedAt = default, UpdatedAt = default };
            context.Boards.Add(board);
            context.BoardAssetPatterns.Add(new BoardAssetPattern
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                SourceId = (await context.Sources.FirstAsync(s => s.Slug == "meshcore-official")).Id,
                Pattern = @"Xiao_nRF52840.*\.uf2$",
            });
            await context.SaveChangesAsync();
            boardId = board.Id;
        }

        await using var verify = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(verify);

        var status = service.Evaluate(
            snapshot, streamId, "1.16.0", VersionSortKey.FromDottedNumber("1.16.0"), boardId);

        Assert.Multiple(() =>
        {
            Assert.That(status.State, Is.EqualTo(UpdateState.UpdateAvailable));
            Assert.That(status.MatchedAsset!.Name, Is.EqualTo("Xiao_nRF52840.uf2"));
            Assert.That(status.MatchedAsset.DownloadUrl, Does.Contain("repeater-v1.17.1"));
        });
    }

    [Test]
    public async Task An_installed_version_newer_than_anything_known_is_not_an_update()
    {
        await SeedOfficialAsync((1, "repeater-v1.17.1"));

        await using var context = this.Db.NewContext();
        var (service, snapshot, streamId) = await LoadAsync(context);

        var status = service.Evaluate(
            snapshot, streamId, "1.18.0", VersionSortKey.FromDottedNumber("1.18.0"), null);

        Assert.That(status.State, Is.EqualTo(UpdateState.UpToDate));
    }

    private SqliteTestDatabase Db = null!;
    private FakeTimeProvider Clock = null!;

    /// <summary>
    /// Sync the official repo with the given tags.
    /// </summary>
    /// <param name="releases"></param>
    private async Task SeedOfficialAsync(params (long Id, string Tag)[] releases)
    {
        var client = new StubGitHubClient();
        client.Returns(releases.Select(r =>
            StubGitHubClient.Release(r.Id, r.Tag, assetNames: ["Xiao_nRF52840.uf2", "rak4631.uf2"]))
            .ToArray());

        await using var context = this.Db.NewContext();
        var sync = new ReleaseSyncService(
            context, client, new VersionSchemeRegistry(), new SyncGate(), this.Clock,
            NullLogger<ReleaseSyncService>.Instance);
        await sync.SyncAsync(await context.Sources.FirstAsync(s => s.Slug == "meshcore-official"));
    }

    /// <summary>
    /// A service, a loaded snapshot, and the official repeater stream.
    /// </summary>
    /// <param name="context"></param>
    private async Task<(UpdateStatusService Service, UpdateStatusService.Snapshot Snapshot, Guid StreamId)>
        LoadAsync(AppDbContext context)
    {
        var service = new UpdateStatusService(context);
        var snapshot = await service.LoadSnapshotAsync();
        var streamId = await context.Streams
            .Where(s => s.Slug == "repeater" && s.Source!.Slug == "meshcore-official")
            .Select(s => s.Id)
            .FirstAsync();
        return (service, snapshot, streamId);
    }
}
