using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services.GitHub;
using MeshMuster.Services.Versioning;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Services;

/// <summary>
/// What one source's sync did.
/// </summary>
public record SyncOutcome
{
    public string SourceSlug { get; init; } = string.Empty;

    public int Added { get; init; }

    public int Updated { get; init; }

    /// <summary>Releases the source's scheme did not recognise.</summary>
    public int Skipped { get; init; }

    /// <summary>True when GitHub answered 304 and nothing was fetched.</summary>
    public bool NotModified { get; init; }

    /// <summary>Why the sync failed, or null when it didn't.</summary>
    public string? Error { get; init; }

    public bool Succeeded => this.Error is null;
}

/// <summary>
/// Pulls releases from every enabled source and stores them against their streams.
/// </summary>
public class ReleaseSyncService
{
    /// <summary>
    /// Initializes a new instance of the ReleaseSyncService class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="client"></param>
    /// <param name="schemes"></param>
    /// <param name="gate"></param>
    /// <param name="clock"></param>
    /// <param name="logger"></param>
    public ReleaseSyncService(
        AppDbContext db,
        IGitHubReleaseClient client,
        VersionSchemeRegistry schemes,
        SyncGate gate,
        TimeProvider clock,
        ILogger<ReleaseSyncService> logger)
    {
        this.Db = db;
        this.Client = client;
        this.Schemes = schemes;
        this.Gate = gate;
        this.Clock = clock;
        this.Logger = logger;
    }

    /// <summary>
    /// Sync every enabled source, in sort order.
    /// </summary>
    /// <param name="ct"></param>
    public async Task<IReadOnlyList<SyncOutcome>> SyncAllAsync(CancellationToken ct = default)
    {
        using var _ = await this.Gate.EnterAsync(ct);

        var sources = await this.Db.Sources
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var outcomes = new List<SyncOutcome>();
        foreach (var source in sources)
            outcomes.Add(await this.SyncOneAsync(source, ct));

        return outcomes;
    }

    /// <summary>
    /// Sync a single source.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="ct"></param>
    public async Task<SyncOutcome> SyncAsync(Source source, CancellationToken ct = default)
    {
        using var _ = await this.Gate.EnterAsync(ct);
        return await this.SyncOneAsync(source, ct);
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The client used to access the source.
    /// </summary>
    private readonly IGitHubReleaseClient Client;

    /// <summary>
    /// The schemes that read each source's tags.
    /// </summary>
    private readonly VersionSchemeRegistry Schemes;

    /// <summary>
    /// The gate that keeps two syncs off the same rows.
    /// </summary>
    private readonly SyncGate Gate;

    /// <summary>
    /// The clock used internally.
    /// </summary>
    private readonly TimeProvider Clock;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<ReleaseSyncService> Logger;

    /// <summary>
    /// Sync one source; the caller already holds the gate.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="ct"></param>
    private async Task<SyncOutcome> SyncOneAsync(Source source, CancellationToken ct)
    {
        var now = this.Clock.GetUtcNow().UtcDateTime;
        var scheme = this.Schemes.For(source.Slug);

        if (scheme is null)
        {
            return await this.FailAsync(source, now,
                $"No version scheme is registered for source '{source.Slug}'.", ct);
        }

        GitHubReleasePage page;
        try
        {
            page = await this.Client.GetReleasesAsync(
                source.GithubOwner, source.GithubRepo, source.Etag, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One bad source shouldn't stop the rest; the error is stored and shown.
            this.Logger.LogWarning(ex, "Polling {Repo} failed", source.RepoSlug);
            return await this.FailAsync(source, now, ex.Message, ct);
        }

        if (page.NotModified)
        {
            source.LastPolledAt = now;
            source.LastPollError = string.Empty;
            source.UpdatedAt = now;
            await this.Db.SaveChangesAsync(ct);
            return new SyncOutcome { SourceSlug = source.Slug, NotModified = true };
        }

        var streams = await this.Db.Streams
            .Where(s => s.SourceId == source.Id)
            .ToDictionaryAsync(s => s.Slug, StringComparer.OrdinalIgnoreCase, ct);

        var existing = await this.Db.Releases
            .Include(r => r.Assets)
            .Where(r => r.Stream!.SourceId == source.Id)
            .ToDictionaryAsync(r => (r.StreamId, r.GithubId), ct);

        int added = 0, updated = 0, skipped = 0;

        // GitHub pages by creation date, so a release added mid-pagination can arrive twice;
        // the two listings can disagree about assets and leave them half-tracked.
        foreach (var incoming in page.Releases.DistinctBy(r => r.Id))
        {
            if (incoming.Draft) { skipped++; continue; }

            var assetNames = incoming.Assets.Select(a => a.Name).ToList();

            if (!scheme.TryParse(incoming.TagName, assetNames, out var parsed))
            {
                skipped++;
                continue;
            }

            var targets = parsed.StreamSlugs
                .Select(slug => streams.GetValueOrDefault(slug))
                .OfType<ReleaseStream>()
                .ToList();

            if (targets.Count == 0)
            {
                skipped++;
                continue;
            }

            foreach (var stream in targets)
            {
                if (existing.TryGetValue((stream.Id, incoming.Id), out var release))
                {
                    updated++;
                }
                else
                {
                    release = new Release
                    {
                        Id = Guid.NewGuid(),
                        GithubId = incoming.Id,
                        StreamId = stream.Id,
                        FirstSeenAt = now,
                    };
                    this.Db.Releases.Add(release);
                    existing[(stream.Id, incoming.Id)] = release;
                    added++;
                }

                release.StreamId = stream.Id;
                release.Tag = incoming.TagName;
                release.Name = incoming.Name ?? string.Empty;
                release.VersionLabel = parsed.VersionLabel;
                release.SortKey = parsed.SortKey;
                release.Detail = parsed.Detail;
                release.IsPrerelease = incoming.Prerelease;
                release.PublishedAt = (incoming.PublishedAt ?? this.Clock.GetUtcNow()).UtcDateTime;
                release.HtmlUrl = incoming.HtmlUrl;

                this.SyncAssets(release, incoming, stream);
            }
        }

        source.Etag = page.Etag;
        source.LastPolledAt = now;
        source.LastPollError = string.Empty;
        source.UpdatedAt = now;

        await this.Db.SaveChangesAsync(ct);

        this.Logger.LogInformation(
            "Synced {Repo}: {Added} new, {Updated} updated, {Skipped} skipped",
            source.RepoSlug, added, updated, skipped);

        return new SyncOutcome
        {
            SourceSlug = source.Slug,
            Added = added,
            Updated = updated,
            Skipped = skipped,
        };
    }

    /// <summary>
    /// Bring a release's assets in line with the ones upstream still serves.
    /// </summary>
    /// <param name="release"></param>
    /// <param name="incoming"></param>
    /// <param name="stream"></param>
    private void SyncAssets(Release release, GitHubRelease incoming, ReleaseStream stream)
    {
        var relevant = incoming.Assets.Where(a => BelongsToStream(a.Name, stream)).ToList();
        var byName = release.Assets.ToDictionary(a => a.Name, StringComparer.Ordinal);

        foreach (var asset in relevant)
        {
            if (byName.TryGetValue(asset.Name, out var existing))
            {
                existing.DownloadUrl = asset.BrowserDownloadUrl;
                existing.SizeBytes = asset.Size;
                byName.Remove(asset.Name);
                continue;
            }

            // Added through the set, not the navigation: an entity that already carries its key
            // looks tracked-as-Modified through a navigation and updates a row never inserted.
            // Fixup still puts it in release.Assets, so byName stays right on a second visit.
            this.Db.ReleaseAssets.Add(new ReleaseAsset
            {
                Id = Guid.NewGuid(),
                ReleaseId = release.Id,
                Release = release,
                Name = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                SizeBytes = asset.Size,
            });
        }

        // Anything left over is gone upstream.
        foreach (var stale in byName.Values)
            this.Db.ReleaseAssets.Remove(stale);
    }

    /// <summary>
    /// Whether an asset belongs in a stream.
    /// </summary>
    /// <param name="assetName"></param>
    /// <param name="stream"></param>
    private static bool BelongsToStream(string assetName, ReleaseStream stream)
    {
        // The bootloader streams serve every role and take everything.
        if (stream.DeviceRole is null) return true;

        var role = AssetRole.Of(assetName);
        return role is null || role == stream.DeviceRole;
    }

    /// <summary>
    /// Record a failed poll against the source and report it.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="now"></param>
    /// <param name="error"></param>
    /// <param name="ct"></param>
    private async Task<SyncOutcome> FailAsync(
        Source source, DateTime now, string error, CancellationToken ct)
    {
        source.LastPolledAt = now;
        source.LastPollError = Truncate(error, 500);
        source.UpdatedAt = now;
        await this.Db.SaveChangesAsync(ct);
        return new SyncOutcome { SourceSlug = source.Slug, Error = error };
    }

    /// <summary>
    /// Cut a string to length; the column is bounded and the message isn't.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="max"></param>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
