using MeshMuster.Data;
using MeshMuster.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Services;

/// <summary>
/// The editable half of a board, as a form submits it.
/// </summary>
public record BoardEdit
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// One board pattern, shown against the newest release it would match.
/// </summary>
public record PatternPreview
{
    public string SourceName { get; init; } = string.Empty;
    public string Pattern { get; init; } = string.Empty;
    public bool IsValid { get; init; }

    /// <summary>The release the matches were drawn from.</summary>
    public string ReleaseLabel { get; init; } = string.Empty;

    public IReadOnlyList<string> Matches { get; init; } = [];
}

/// <summary>
/// A managed way to read and write boards and their asset patterns.
/// </summary>
public class BoardService
{
    /// <summary>
    /// Initializes a new instance of the BoardService class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="clock"></param>
    public BoardService(AppDbContext db, TimeProvider clock)
    {
        this.Db = db;
        this.Clock = clock;
    }

    /// <summary>
    /// Every board, by name, with its patterns loaded.
    /// </summary>
    /// <param name="ct"></param>
    public Task<List<Board>> ListAsync(CancellationToken ct = default) =>
        this.Db.Boards
            .Include(b => b.AssetPatterns)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

    /// <summary>
    /// Create a board from an edit.
    /// </summary>
    /// <param name="edit"></param>
    /// <param name="ct"></param>
    public async Task<Board> CreateAsync(BoardEdit edit, CancellationToken ct = default)
    {
        var now = this.Clock.GetUtcNow().UtcDateTime;
        var board = new Board
        {
            Id = Guid.NewGuid(),
            Name = edit.Name.Trim(),
            Description = edit.Description.Trim(),
            IsActive = edit.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
        };
        this.Db.Boards.Add(board);
        await this.Db.SaveChangesAsync(ct);
        return board;
    }

    /// <summary>
    /// Apply an edit to an existing board; false when there is no such board.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="edit"></param>
    /// <param name="ct"></param>
    public async Task<bool> UpdateAsync(Guid id, BoardEdit edit, CancellationToken ct = default)
    {
        var board = await this.Db.Boards.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return false;

        board.Name = edit.Name.Trim();
        board.Description = edit.Description.Trim();
        board.IsActive = edit.IsActive;
        board.UpdatedAt = this.Clock.GetUtcNow().UtcDateTime;
        await this.Db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Delete a board; false when there is no such board.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var board = await this.Db.Boards.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (board is null) return false;

        this.Db.Boards.Remove(board);
        await this.Db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Set a board's pattern for one source; a blank pattern clears it.
    /// </summary>
    /// <param name="boardId"></param>
    /// <param name="sourceId"></param>
    /// <param name="pattern"></param>
    /// <param name="ct"></param>
    public async Task SetPatternAsync(
        Guid boardId, Guid sourceId, string pattern, CancellationToken ct = default)
    {
        var existing = await this.Db.BoardAssetPatterns
            .FirstOrDefaultAsync(p => p.BoardId == boardId && p.SourceId == sourceId, ct);

        var trimmed = pattern.Trim();

        if (trimmed.Length == 0)
        {
            if (existing is not null) this.Db.BoardAssetPatterns.Remove(existing);
        }
        else if (existing is null)
        {
            this.Db.BoardAssetPatterns.Add(new BoardAssetPattern
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                SourceId = sourceId,
                Pattern = trimmed,
            });
        }
        else
        {
            existing.Pattern = trimmed;
        }

        await this.Db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// What a board's patterns match today, one row per source.
    /// </summary>
    /// <param name="boardId"></param>
    /// <param name="ct"></param>
    public async Task<IReadOnlyList<PatternPreview>> PreviewAsync(
        Guid boardId, CancellationToken ct = default)
    {
        var patterns = await this.Db.BoardAssetPatterns
            .AsNoTracking()
            .Where(p => p.BoardId == boardId)
            .ToDictionaryAsync(p => p.SourceId, p => p.Pattern, ct);

        var sources = await this.Db.Sources.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var previews = new List<PatternPreview>();

        foreach (var source in sources)
        {
            var pattern = patterns.GetValueOrDefault(source.Id, string.Empty);

            var newest = await this.Db.Releases
                .AsNoTracking()
                .Include(r => r.Assets)
                .Where(r => r.Stream!.SourceId == source.Id)
                .Where(r => source.IncludePrereleases || !r.IsPrerelease)
                .OrderByDescending(r => r.SortKey)
                .FirstOrDefaultAsync(ct);

            var matches = newest is null
                ? []
                : AssetMatcher.Match(newest.Assets, pattern).Select(a => a.Name).ToList();

            previews.Add(new PatternPreview
            {
                SourceName = source.DisplayName,
                Pattern = pattern,

                // A blank pattern is allowed; only a broken one is invalid.
                IsValid = pattern.Length == 0 || AssetMatcher.IsValidPattern(pattern),
                ReleaseLabel = newest?.VersionLabel ?? "no releases synced",
                Matches = matches,
            });
        }

        return previews;
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The clock used internally.
    /// </summary>
    private readonly TimeProvider Clock;
}
