using MeshMuster.Data;
using MeshMuster.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Services;

/// <summary>
/// Judges each device against the newest release on its channel.
/// </summary>
public class UpdateStatusService
{
    /// <summary>
    /// Initializes a new instance of the UpdateStatusService class.
    /// </summary>
    /// <param name="db"></param>
    public UpdateStatusService(AppDbContext db)
    {
        this.Db = db;
    }

    /// <summary>
    /// One prefetch for the whole page, instead of two queries per device.
    /// </summary>
    public record Snapshot
    {
        public IReadOnlyDictionary<Guid, Release> LatestByStream { get; init; } =
            new Dictionary<Guid, Release>();

        /// <summary>Descending, so counting what's newer is a walk from the front.</summary>
        public IReadOnlyDictionary<Guid, IReadOnlyList<string>> SortKeysByStream { get; init; } =
            new Dictionary<Guid, IReadOnlyList<string>>();

        public IReadOnlyDictionary<Guid, ReleaseStream> StreamsById { get; init; } =
            new Dictionary<Guid, ReleaseStream>();

        public IReadOnlyDictionary<Guid, Source> SourcesById { get; init; } =
            new Dictionary<Guid, Source>();

        public IReadOnlyDictionary<(Guid BoardId, Guid SourceId), string> BoardPatterns { get; init; } =
            new Dictionary<(Guid, Guid), string>();
    }

    /// <summary>
    /// Load everything the page needs to judge every device.
    /// </summary>
    /// <param name="ct"></param>
    public async Task<Snapshot> LoadSnapshotAsync(CancellationToken ct = default)
    {
        var sources = await this.Db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var streams = await this.Db.Streams.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);

        var candidates = await this.Db.Releases
            .AsNoTracking()
            .Include(r => r.Assets)
            .ToListAsync(ct);

        var latest = new Dictionary<Guid, Release>();
        var sortKeys = new Dictionary<Guid, List<string>>();

        foreach (var release in candidates)
        {
            if (!streams.TryGetValue(release.StreamId, out var stream)) continue;
            if (!sources.TryGetValue(stream.SourceId, out var source)) continue;
            if (release.IsPrerelease && !source.IncludePrereleases) continue;

            if (!sortKeys.TryGetValue(release.StreamId, out var keys))
                sortKeys[release.StreamId] = keys = [];
            keys.Add(release.SortKey);

            if (!latest.TryGetValue(release.StreamId, out var best) ||
                string.CompareOrdinal(release.SortKey, best.SortKey) > 0)
            {
                latest[release.StreamId] = release;
            }
        }

        var patterns = await this.Db.BoardAssetPatterns
            .AsNoTracking()
            .ToDictionaryAsync(p => (p.BoardId, p.SourceId), p => p.Pattern, ct);

        var orderedKeys = sortKeys.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value
                .OrderByDescending(k => k, StringComparer.Ordinal).ToList());

        return new Snapshot
        {
            LatestByStream = latest,
            SortKeysByStream = orderedKeys,
            StreamsById = streams,
            SourcesById = sources,
            BoardPatterns = patterns,
        };
    }

    /// <summary>
    /// Where one installed version stands against its channel.
    /// </summary>
    /// <param name="snapshot"></param>
    /// <param name="streamId"></param>
    /// <param name="installedLabel"></param>
    /// <param name="installedSortKey"></param>
    /// <param name="boardId"></param>
    public DeviceUpdateStatus Evaluate(
        Snapshot snapshot,
        Guid? streamId,
        string installedLabel,
        string installedSortKey,
        Guid? boardId)
    {
        if (streamId is null || !snapshot.StreamsById.TryGetValue(streamId.Value, out var stream))
            return DeviceUpdateStatus.None(installedLabel);

        var sourceName = snapshot.SourcesById.TryGetValue(stream.SourceId, out var source)
            ? source.DisplayName
            : string.Empty;

        // A failed poll outranks the comparison; what we hold may already be out of date.
        if (source is not null && source.LastPollError.Length > 0)
        {
            return new DeviceUpdateStatus
            {
                State = UpdateState.SourceStale,
                InstalledLabel = installedLabel,
                Latest = snapshot.LatestByStream.GetValueOrDefault(streamId.Value),
                SourceName = sourceName,
                StaleReason = source.LastPollError,
            };
        }

        if (!snapshot.LatestByStream.TryGetValue(streamId.Value, out var latest))
        {
            return new DeviceUpdateStatus
            {
                State = UpdateState.Unknown,
                InstalledLabel = installedLabel,
                SourceName = sourceName,
            };
        }

        // No sort key means the label matched no release, so there is nothing to compare.
        if (string.IsNullOrEmpty(installedSortKey))
        {
            return new DeviceUpdateStatus
            {
                State = UpdateState.Unknown,
                InstalledLabel = installedLabel,
                Latest = latest,
                MatchedAsset = MatchAsset(snapshot, latest, boardId, stream.SourceId),
                SourceName = sourceName,
            };
        }

        if (string.CompareOrdinal(installedSortKey, latest.SortKey) >= 0)
        {
            return new DeviceUpdateStatus
            {
                State = UpdateState.UpToDate,
                InstalledLabel = installedLabel,
                Latest = latest,
                SourceName = sourceName,
            };
        }

        return new DeviceUpdateStatus
        {
            State = UpdateState.UpdateAvailable,
            InstalledLabel = installedLabel,
            Latest = latest,
            ReleasesBehind = CountBehind(snapshot, streamId.Value, installedSortKey),
            MatchedAsset = MatchAsset(snapshot, latest, boardId, stream.SourceId),
            SourceName = sourceName,
        };
    }

    /// <summary>
    /// Every release on a stream, newest first.
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="includePrereleases"></param>
    /// <param name="ct"></param>
    public async Task<IReadOnlyList<Release>> ReleasesForStreamAsync(
        Guid streamId, bool includePrereleases, CancellationToken ct = default)
    {
        var query = this.Db.Releases.AsNoTracking().Where(r => r.StreamId == streamId);
        if (!includePrereleases) query = query.Where(r => !r.IsPrerelease);

        return await query
            .OrderByDescending(r => r.SortKey)
            .ThenByDescending(r => r.PublishedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// How many newer releases there are; distinct, so a version published twice counts once.
    /// </summary>
    /// <param name="snapshot"></param>
    /// <param name="streamId"></param>
    /// <param name="installedSortKey"></param>
    private static int CountBehind(Snapshot snapshot, Guid streamId, string installedSortKey)
    {
        if (!snapshot.SortKeysByStream.TryGetValue(streamId, out var keys)) return 0;

        return keys
            .Where(k => string.CompareOrdinal(k, installedSortKey) > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>
    /// The file to flash, when the board carries a pattern that picks one.
    /// </summary>
    /// <param name="snapshot"></param>
    /// <param name="release"></param>
    /// <param name="boardId"></param>
    /// <param name="sourceId"></param>
    private static ReleaseAsset? MatchAsset(
        Snapshot snapshot, Release release, Guid? boardId, Guid sourceId)
    {
        if (boardId is null) return null;
        if (!snapshot.BoardPatterns.TryGetValue((boardId.Value, sourceId), out var pattern))
            return null;

        return AssetMatcher.Match(release.Assets, pattern).FirstOrDefault();
    }
}
