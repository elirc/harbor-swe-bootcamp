using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class PersonalConversationsController(HarborDbContext db) : ControllerBase
{
    private Task<bool> HasParent(Guid id, CancellationToken ct) => db.Conversations.AnyAsync(
        c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct);
    private IQueryable<ConversationBookmark> Bookmarks(Guid id) => db.ConversationBookmarks.Where(b =>
        b.WorkspaceId == User.GetWorkspaceId() && b.TeammateId == User.GetTeammateId() && b.ConversationId == id);
    private IQueryable<ReplyDraft> Drafts(Guid id) => db.ReplyDrafts.Where(d =>
        d.WorkspaceId == User.GetWorkspaceId() && d.OwnerTeammateId == User.GetTeammateId() && d.ConversationId == id);
    private static DraftResponse Map(ReplyDraft row) => new(row.Body, row.Version, row.UpdatedAt);
    private ObjectResult StaleDraft() => Conflict(new ProblemDetails
    { Status = 409, Title = "Stale draft", Detail = "Reload your draft, compare the text, and retry with its current version." });

    [HttpPut("api/conversations/{id:guid}/bookmark")]
    public async Task<IActionResult> Bookmark(Guid id, CancellationToken ct)
    {
        if (!await HasParent(id, ct)) return NotFound();
        if (await Bookmarks(id).AnyAsync(ct)) return NoContent();
        var row = new ConversationBookmark { WorkspaceId = User.GetWorkspaceId(), TeammateId = User.GetTeammateId(), ConversationId = id };
        db.ConversationBookmarks.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        {
            db.Entry(row).State = EntityState.Detached;
            if (!await Bookmarks(id).AnyAsync(ct)) throw;
        }
        return NoContent();
    }

    [HttpDelete("api/conversations/{id:guid}/bookmark")]
    public async Task<IActionResult> Unbookmark(Guid id, CancellationToken ct)
    {
        if (!await HasParent(id, ct)) return NotFound();
        await Bookmarks(id).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/bookmarked-conversations")]
    public async Task<ActionResult<List<ConversationSummaryResponse>>> ListBookmarks(Guid workspaceId, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        var rows = await db.ConversationBookmarks.AsNoTracking()
            .Where(b => b.WorkspaceId == workspaceId && b.TeammateId == User.GetTeammateId()
                && b.Conversation!.WorkspaceId == workspaceId)
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.ConversationId)
            .Include(b => b.Conversation!).ThenInclude(c => c.Tags).ThenInclude(t => t.Tag)
            .ToPageAsync(paging, Response, ct);
        return rows.Select(b => b.Conversation!.ToSummaryResponse()).ToList();
    }

    [HttpGet("api/conversations/{id:guid}/my-draft")]
    public async Task<ActionResult<DraftResponse>> GetDraft(Guid id, CancellationToken ct)
    {
        if (!await HasParent(id, ct)) return NotFound();
        var row = await Drafts(id).AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null ? NotFound() : Map(row);
    }

    [HttpPut("api/conversations/{id:guid}/my-draft")]
    public async Task<ActionResult<DraftResponse>> SaveDraft(Guid id, SaveDraftRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion == Guid.Empty) return BadRequest(new ProblemDetails { Status = 400, Title = "ExpectedVersion cannot be empty" });
        if (!await HasParent(id, ct)) return NotFound();
        var row = await Drafts(id).SingleOrDefaultAsync(ct);
        var create = row is null;
        if (create)
        {
            if (request.ExpectedVersion is not null) return StaleDraft();
            row = new ReplyDraft { WorkspaceId = User.GetWorkspaceId(), OwnerTeammateId = User.GetTeammateId(), ConversationId = id, Body = request.Body };
            db.ReplyDrafts.Add(row);
        }
        else
        {
            if (row!.Version != request.ExpectedVersion) return StaleDraft();
            if (row.Body != request.Body)
            {
                row.Body = request.Body;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (create && ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            db.Entry(row!).State = EntityState.Detached;
            if (!await Drafts(id).AnyAsync(ct)) throw;
            return StaleDraft();
        }
        return create ? CreatedAtAction(nameof(GetDraft), new { id }, Map(row!)) : Map(row!);
    }

    [HttpDelete("api/conversations/{id:guid}/my-draft")]
    public async Task<IActionResult> DeleteDraft(Guid id, [FromQuery, Required, NonEmptyGuid] Guid? expectedVersion, CancellationToken ct)
    {
        if (!await HasParent(id, ct)) return NotFound();
        var row = await Drafts(id).SingleOrDefaultAsync(ct);
        if (row is null) return NotFound();
        if (row.Version != expectedVersion) return StaleDraft();
        db.ReplyDrafts.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
