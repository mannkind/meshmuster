using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Pages;

/// <summary>
/// One source as the page lists it, with what has been synced from it.
/// </summary>
public record SourceRow
{
    public Source Source { get; init; } = null!;
    public int ReleaseCount { get; init; }

    /// <summary>The newest release we hold, honouring the prerelease setting.</summary>
    public Release? Latest { get; init; }

    public IReadOnlyList<ReleaseStream> Streams { get; init; } = [];
}

/// <summary>
/// The sources, their streams, and the Sync button.
/// </summary>
public class SourcesModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the SourcesModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="sync"></param>
    public SourcesModel(AppDbContext db, ReleaseSyncService sync)
    {
        this.Db = db;
        this.Sync = sync;
    }

    public IReadOnlyList<SourceRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => await this.LoadAsync(ct);

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken ct)
    {
        var outcomes = await this.Sync.SyncAllAsync(ct);

        var failed = outcomes.Where(o => !o.Succeeded).Select(o => o.SourceSlug).ToList();
        var added = outcomes.Sum(o => o.Added);

        this.TempData["Flash"] = failed.Count > 0
            ? $"Checked all sources; {string.Join(", ", failed)} failed. {added} new release(s)."
            : added > 0
                ? $"Checked all sources. {added} new release(s)."
                : "Checked all sources. Nothing new.";

        return this.RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid sourceId, CancellationToken ct)
    {
        var source = await this.Db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null)
        {
            return this.NotFound();
        }

        source.IsEnabled = !source.IsEnabled;
        await this.Db.SaveChangesAsync(ct);
        this.TempData["Flash"] =
            $"{source.DisplayName} {(source.IsEnabled ? "enabled" : "disabled")}.";
        return this.RedirectToPage();
    }

    public async Task<IActionResult> OnPostPrereleasesAsync(Guid sourceId, CancellationToken ct)
    {
        var source = await this.Db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null)
        {
            return this.NotFound();
        }

        source.IncludePrereleases = !source.IncludePrereleases;
        await this.Db.SaveChangesAsync(ct);
        this.TempData["Flash"] = source.IncludePrereleases
            ? $"{source.DisplayName} now includes prereleases."
            : $"{source.DisplayName} now ignores prereleases. If every release there is flagged " +
              "prerelease, that channel will look empty.";
        return this.RedirectToPage();
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The sync service used internally.
    /// </summary>
    private readonly ReleaseSyncService Sync;

    /// <summary>
    /// Load every row; three flat reads, then joined in memory.
    /// </summary>
    /// <param name="ct"></param>
    private async Task LoadAsync(CancellationToken ct)
    {
        var sources = await this.Db.Sources.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var streams = await this.Db.Streams.AsNoTracking().ToListAsync(ct);
        var releases = await this.Db.Releases.AsNoTracking().ToListAsync(ct);

        var streamsBySource = streams.ToLookup(s => s.SourceId);
        var sourceByStream = streams.ToDictionary(s => s.Id, s => s.SourceId);

        var rows = new List<SourceRow>();
        foreach (var source in sources)
        {
            var own = releases
                .Where(r => sourceByStream.TryGetValue(r.StreamId, out var sid) && sid == source.Id)
                .ToList();

            var latest = own
                .Where(r => source.IncludePrereleases || !r.IsPrerelease)
                .OrderByDescending(r => r.SortKey, StringComparer.Ordinal)
                .FirstOrDefault();

            rows.Add(new SourceRow
            {
                Source = source,
                ReleaseCount = own.Count,
                Latest = latest,
                Streams = streamsBySource[source.Id].ToList(),
            });
        }

        this.Rows = rows;
    }
}
