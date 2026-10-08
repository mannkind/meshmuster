using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using MeshMuster.Services.GitHub;
using MeshMuster.Services.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MeshMuster.Tests;

/// <summary>
/// Syncing releases from a stubbed GitHub into a real SQLite database.
/// </summary>
public class ReleaseSyncServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        this.Db = new SqliteTestDatabase();
        await this.Db.MigrateAsync();
        this.Client = new StubGitHubClient();
        this.Clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        this.Gate = new SyncGate();
    }

    [TearDown]
    public void TearDown()
    {
        this.Gate.Dispose();
        this.Db.Dispose();
    }

    [Test]
    public async Task Stores_releases_and_assigns_them_to_the_right_stream()
    {
        this.Client.Returns(
            StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "repeater.uf2"),
            StubGitHubClient.Release(2, "companion-v1.17.1", assetNames: "companion.uf2"));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        await using var verify = this.Db.NewContext();
        var releases = await verify.Releases.Include(r => r.Stream).ToListAsync();

        Assert.That(releases, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(releases.Single(r => r.Tag == "repeater-v1.17.1").Stream!.Slug,
                Is.EqualTo("repeater"));
            Assert.That(releases.Single(r => r.Tag == "companion-v1.17.1").Stream!.Slug,
                Is.EqualTo("companion"));
        });
    }

    [Test]
    public async Task Drops_tags_that_belong_to_no_tracked_stream()
    {
        this.Client.Returns(
            StubGitHubClient.Release(1, "repeater-v1.17.1"),
            StubGitHubClient.Release(2, "lora-ota-v1.17.1"),
            StubGitHubClient.Release(3, "room-v6"));

        SyncOutcome outcome;
        await using (var context = this.Db.NewContext())
            outcome = await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Added, Is.EqualTo(1));
            Assert.That(outcome.Skipped, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Syncing_twice_yields_one_row_per_release()
    {
        this.Client
            .Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "a.uf2"))
            .Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "a.uf2"));

        for (var i = 0; i < 2; i++)
        {
            await using var context = this.Db.NewContext();
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.Count("releases"), Is.EqualTo(1));
            Assert.That(await this.Db.Count("release_assets"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_failed_fetch_preserves_existing_releases_and_records_the_error()
    {
        this.Client
            .Returns(StubGitHubClient.Release(1, "repeater-v1.17.1"))
            .Throws("GitHub is down");

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        SyncOutcome outcome;
        await using (var context = this.Db.NewContext())
            outcome = await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        await using var verify = this.Db.NewContext();
        var source = await SourceAsync(verify, "meshcore-official");

        Assert.Multiple(async () =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(await this.Db.Count("releases"), Is.EqualTo(1));
            Assert.That(source.LastPollError, Does.Contain("GitHub is down"));
        });
    }

    [Test]
    public async Task A_successful_poll_clears_a_previous_error()
    {
        this.Client
            .Throws("transient")
            .Returns(StubGitHubClient.Release(1, "repeater-v1.17.1"));

        for (var i = 0; i < 2; i++)
        {
            await using var context = this.Db.NewContext();
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));
        }

        await using var verify = this.Db.NewContext();
        Assert.That((await SourceAsync(verify, "meshcore-official")).LastPollError, Is.Empty);
    }

    [Test]
    public async Task Sends_the_stored_etag_and_leaves_data_alone_on_304()
    {
        this.Client
            .ReturnsWithEtag("\"abc\"", StubGitHubClient.Release(1, "repeater-v1.17.1"))
            .ReturnsNotModified("\"abc\"");

        for (var i = 0; i < 2; i++)
        {
            await using var context = this.Db.NewContext();
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(this.Client.SeenEtags[0], Is.Empty);
            Assert.That(this.Client.SeenEtags[1], Is.EqualTo("\"abc\""));
            Assert.That(await this.Db.Count("releases"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Records_prerelease_flag_so_channels_can_filter_on_it()
    {
        this.Client.Returns(
            StubGitHubClient.Release(1, "v1.17.1.5-halo-keymind-cascade-dev-26303793", prerelease: true));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-mikecarper"));

        await using var verify = this.Db.NewContext();
        var release = await verify.Releases.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(release.IsPrerelease, Is.True);
            Assert.That(release.VersionLabel, Is.EqualTo("1.17.1.5"));
        });
    }

    [Test]
    public async Task Removes_assets_that_disappeared_upstream()
    {
        this.Client
            .Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: ["a.uf2", "b.uf2"]))
            .Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "a.uf2"));

        for (var i = 0; i < 2; i++)
        {
            await using var context = this.Db.NewContext();
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));
        }

        Assert.That(await this.Db.Count("release_assets"), Is.EqualTo(1));
    }

    [Test]
    public async Task An_omnibus_release_lands_in_both_streams_with_its_own_assets_in_each()
    {
        // The older mikecarper tags shipped every role's firmware in one release.
        this.Client.Returns(StubGitHubClient.Release(
            1, "v1.17.1.1-halo-keymind-cascade-759a35fc", prerelease: false,
            assetNames:
            [
                "Xiao_companion-v1.17.1.1-759a35fc.uf2",
                "Heltec_t096_repeater-v1.17.1.1-759a35fc.uf2",
                "Heltec_t096_room_server-v1.17.1.1-759a35fc.uf2",
            ]));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-mikecarper"));

        await using var verify = this.Db.NewContext();
        var rows = await verify.Releases
            .Include(r => r.Assets)
            .Include(r => r.Stream)
            .Where(r => r.GithubId == 1)
            .ToListAsync();

        var companion = rows.Single(r => r.Stream!.Slug == "companion");
        var repeater = rows.Single(r => r.Stream!.Slug == "repeater");
        var room = rows.Single(r => r.Stream!.Slug == "room");

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(companion.VersionLabel, Is.EqualTo("1.17.1.1"));
            Assert.That(repeater.VersionLabel, Is.EqualTo("1.17.1.1"));
            Assert.That(room.VersionLabel, Is.EqualTo("1.17.1.1"));

            Assert.That(companion.Assets.Select(a => a.Name),
                Is.EquivalentTo(new[] { "Xiao_companion-v1.17.1.1-759a35fc.uf2" }));
            Assert.That(repeater.Assets.Select(a => a.Name),
                Is.EquivalentTo(new[] { "Heltec_t096_repeater-v1.17.1.1-759a35fc.uf2" }));
            Assert.That(room.Assets.Select(a => a.Name),
                Is.EquivalentTo(new[] { "Heltec_t096_room_server-v1.17.1.1-759a35fc.uf2" }));
        });
    }

    [Test]
    public async Task Re_syncing_an_omnibus_release_does_not_duplicate_either_stream_copy()
    {
        for (var i = 0; i < 2; i++)
        {
            this.Client.Returns(StubGitHubClient.Release(
                1, "v1.17.1.1-halo-keymind-cascade-759a35fc", prerelease: false,
                assetNames: ["Xiao_companion-v1.uf2", "Heltec_t096_repeater-v1.uf2"]));

            await using var context = this.Db.NewContext();
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-mikecarper"));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.Count("releases"), Is.EqualTo(2));
            Assert.That(await this.Db.Count("release_assets"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task A_companion_only_bare_tag_does_not_reach_the_repeater_stream()
    {
        this.Client.Returns(StubGitHubClient.Release(
            1, "v1.17.1.5-halo-keymind-cascade-dev-26303793", prerelease: true,
            assetNames: ["Xiao_companion-v1.17.1.5.uf2"]));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-mikecarper"));

        await using var verify = this.Db.NewContext();
        var streams = await verify.Releases.Include(r => r.Stream)
            .Select(r => r.Stream!.Slug).ToListAsync();

        Assert.That(streams, Is.EqualTo(new[] { "companion" }));
    }

    [Test]
    public async Task Skips_disabled_sources()
    {
        await using (var context = this.Db.NewContext())
        {
            foreach (var source in await context.Sources.ToListAsync())
                source.IsEnabled = false;
            await context.SaveChangesAsync();
        }

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAllAsync();

        Assert.That(this.Client.CallCount, Is.Zero);
    }

    [Test]
    public async Task Overlapping_syncs_are_serialised()
    {
        this.Client.Returns(StubGitHubClient.Release(
            1, "repeater-v1.17.1", assetNames: ["repeater_a.uf2", "repeater_b.uf2"]));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        // Both passes see repeater_b.uf2 as gone upstream; only one of them can delete the row.
        this.Client.Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "repeater_a.uf2"));
        this.Client.Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "repeater_a.uf2"));

        var probe = new OverlapProbe(this.Client);
        var go = new TaskCompletionSource();
        var ready = 0;

        async Task SyncAsync()
        {
            await using var context = this.Db.NewContext();
            var service = new ReleaseSyncService(
                context, probe, new VersionSchemeRegistry(), this.Gate, this.Clock,
                NullLogger<ReleaseSyncService>.Instance);
            var source = await SourceAsync(context, "meshcore-official");

            // Both sides finish opening their connection before either starts, so the overlap
            // the gate has to prevent is real and not left to the thread pool to arrange.
            if (Interlocked.Increment(ref ready) == 2) go.TrySetResult();
            await go.Task;

            await service.SyncAsync(source);
        }

        Assert.That(async () => await Task.WhenAll(Task.Run(SyncAsync), Task.Run(SyncAsync)),
            Throws.Nothing);

        await using var verify = this.Db.NewContext();
        var remaining = await verify.ReleaseAssets.Select(a => a.Name).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(probe.MaxInFlight, Is.EqualTo(1));
            Assert.That(remaining, Is.EqualTo(new[] { "repeater_a.uf2" }));
        });
    }

    [Test]
    public async Task Assets_uploaded_after_a_release_was_first_seen_are_stored()
    {
        // A big release is published before its assets finish uploading, so the poll that first
        // stores it sees only some of them.
        this.Client.Returns(StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "repeater_a.uf2"));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        this.Client.Returns(StubGitHubClient.Release(
            1, "repeater-v1.17.1", assetNames: ["repeater_a.uf2", "repeater_b.uf2"]));

        await using (var context = this.Db.NewContext())
            await Service(context).SyncAsync(await SourceAsync(context, "meshcore-official"));

        await using var verify = this.Db.NewContext();
        Assert.That(await verify.ReleaseAssets.Select(a => a.Name).OrderBy(n => n).ToListAsync(),
            Is.EqualTo(new[] { "repeater_a.uf2", "repeater_b.uf2" }));
    }

    [Test]
    public async Task A_release_listed_twice_in_one_page_is_only_visited_once()
    {
        // GitHub pages by creation date, so a publisher adding releases mid-pagination can serve
        // the same release on two pages. Visiting it twice is wasted work over the hundreds of
        // assets these releases carry, and the two listings need not agree about them.
        this.Client.Returns(
            StubGitHubClient.Release(
                1, "repeater-v1.17.1", assetNames: ["repeater_a.uf2", "repeater_b.uf2"]),
            StubGitHubClient.Release(1, "repeater-v1.17.1", assetNames: "repeater_a.uf2"));

        await using var context = this.Db.NewContext();
        var outcome = await Service(context)
            .SyncAsync(await SourceAsync(context, "meshcore-official"));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Added, Is.EqualTo(1));
            Assert.That(outcome.Updated, Is.Zero);
        });
    }

    /// <summary>
    /// The database under test.
    /// </summary>
    private SqliteTestDatabase Db = null!;

    /// <summary>
    /// The stubbed source.
    /// </summary>
    private StubGitHubClient Client = null!;

    /// <summary>
    /// The clock used internally.
    /// </summary>
    private FakeTimeProvider Clock = null!;

    /// <summary>
    /// The gate under test where two syncs race.
    /// </summary>
    private SyncGate Gate = null!;

    /// <summary>
    /// A sync service over the given context.
    /// </summary>
    /// <param name="context"></param>
    private ReleaseSyncService Service(AppDbContext context) =>
        new(context, this.Client, new VersionSchemeRegistry(), this.Gate, this.Clock,
            NullLogger<ReleaseSyncService>.Instance);

    /// <summary>
    /// A seeded source by slug.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="slug"></param>
    private async Task<Source> SourceAsync(AppDbContext context, string slug) =>
        await context.Sources.FirstAsync(s => s.Slug == slug);

    /// <summary>
    /// Counts how many syncs are in flight at once; a missing gate shows up as two.
    /// </summary>
    private class OverlapProbe : IGitHubReleaseClient
    {
        /// <summary>
        /// Initializes a new instance of the OverlapProbe class.
        /// </summary>
        /// <param name="inner"></param>
        public OverlapProbe(IGitHubReleaseClient inner)
        {
            this.Inner = inner;
        }

        public int MaxInFlight { get; private set; }

        /// <inheritdoc />
        public async Task<GitHubReleasePage> GetReleasesAsync(
            string owner, string repo, string etag, CancellationToken ct = default)
        {
            lock (this.Sync) this.MaxInFlight = Math.Max(this.MaxInFlight, ++this.InFlight);

            // Hold the door open for a second caller, so overlap shows up rather than timing luck.
            if (Interlocked.Increment(ref this.Arrivals) == 2) this.BothArrived.TrySetResult();
            else await Task.WhenAny(this.BothArrived.Task, Task.Delay(TimeSpan.FromMilliseconds(500), ct));

            lock (this.Sync) this.InFlight--;
            return await this.Inner.GetReleasesAsync(owner, repo, etag, ct);
        }

        /// <summary>
        /// The real client this wraps.
        /// </summary>
        private readonly IGitHubReleaseClient Inner;

        /// <summary>
        /// Completed once two callers have arrived.
        /// </summary>
        private readonly TaskCompletionSource BothArrived = new();

        /// <summary>
        /// Guards the in-flight count.
        /// </summary>
        private readonly Lock Sync = new();

        /// <summary>
        /// How many callers have arrived in total.
        /// </summary>
        private int Arrivals;

        /// <summary>
        /// How many callers are inside right now.
        /// </summary>
        private int InFlight;
    }
}
