using MeshMuster.Data;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Pages;

/// <summary>
/// The boards, and the asset pattern each one uses per source.
/// </summary>
public class BoardsModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the BoardsModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="boards"></param>
    public BoardsModel(AppDbContext db, BoardService boards)
    {
        this.Db = db;
        this.Service = boards;
    }

    public IReadOnlyList<Board> Boards { get; private set; } = [];
    public IReadOnlyList<Source> Sources { get; private set; } = [];
    public IReadOnlyDictionary<Guid, int> DeviceCounts { get; private set; } =
        new Dictionary<Guid, int>();

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Description { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken ct) => await this.LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            this.TempData["Flash"] = "A board needs a name.";
            return this.RedirectToPage();
        }

        await this.Service.CreateAsync(
            new BoardEdit { Name = this.Name, Description = this.Description ?? string.Empty },
            ct
        );
        this.TempData["Flash"] = $"Board '{this.Name.Trim()}' added.";
        return this.RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync(
        Guid boardId, string name, string description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            this.TempData["Flash"] = "A board needs a name.";
            return this.RedirectToPage();
        }

        // Checked here, not caught: the unique index would surface as a 500.
        var clash = await this.Db.Boards.AnyAsync(
            b => b.Id != boardId && b.Name == name.Trim(), ct
        );
        if (clash)
        {
            this.TempData["Flash"] = $"There is already a board called '{name.Trim()}'.";
            return this.RedirectToPage();
        }

        var edit = new BoardEdit { Name = name, Description = description ?? string.Empty };
        if (!await this.Service.UpdateAsync(boardId, edit, ct))
        {
            return this.NotFound();
        }

        this.TempData["Flash"] = "Board saved.";
        return this.RedirectToPage();
    }

    public async Task<IActionResult> OnPostPatternAsync(
        Guid boardId, Guid sourceId, string pattern, CancellationToken ct)
    {
        if (pattern is { Length: > 0 } && !AssetMatcher.IsValidPattern(pattern))
        {
            this.TempData["Flash"] = "That pattern is not a valid regular expression.";
            return this.RedirectToPage();
        }

        await this.Service.SetPatternAsync(boardId, sourceId, pattern ?? string.Empty, ct);
        this.TempData["Flash"] = "Pattern saved.";
        return this.RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid boardId, CancellationToken ct)
    {
        await this.Service.DeleteAsync(boardId, ct);
        this.TempData["Flash"] = "Board deleted. Devices that used it now have no board.";
        return this.RedirectToPage();
    }

    /// <summary>
    /// A board's pattern for one source, blank when it has none.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="sourceId"></param>
    public string PatternFor(Board board, Guid sourceId) =>
        board.AssetPatterns.FirstOrDefault(p => p.SourceId == sourceId)?.Pattern ?? string.Empty;

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The board service used internally.
    /// </summary>
    private readonly BoardService Service;

    /// <summary>
    /// Load everything the page shows.
    /// </summary>
    /// <param name="ct"></param>
    private async Task LoadAsync(CancellationToken ct)
    {
        this.Boards = await this.Service.ListAsync(ct);
        this.Sources = await this.Db.Sources.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        this.DeviceCounts = await this.Db.Devices
            .Where(d => d.BoardId != null)
            .GroupBy(d => d.BoardId!.Value)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BoardId, x => x.Count, ct);
    }
}
