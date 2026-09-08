using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class CannedRepliesController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/canned-replies")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CannedReplyResponse>> Create(
        Guid workspaceId, CreateCannedReplyRequest request)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var shortcut = request.Shortcut.Trim().ToLowerInvariant();
        if (await db.CannedReplies.AnyAsync(r => r.WorkspaceId == workspaceId && r.Shortcut == shortcut))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate shortcut",
                Detail = $"A canned reply with shortcut '{shortcut}' already exists in this workspace.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        var reply = new CannedReply
        {
            WorkspaceId = workspaceId,
            Shortcut = shortcut,
            Title = request.Title,
            Body = request.Body,
        };
        db.CannedReplies.Add(reply);
        ConfigurationAudits.CannedReply(db, workspaceId, User.GetTeammateId(), reply.Id, ConfigurationAuditAction.Created, ["Shortcut", "Title", "Body"], clock.GetUtcNow());
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = reply.Id }, reply.ToResponse());
    }

    /// <summary>Lists canned replies; optional search on shortcut/title/body.</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/canned-replies")]
    public async Task<ActionResult<List<CannedReplyResponse>>> List(
        Guid workspaceId, [FromQuery] string? q, [FromQuery] string? shortcut,
        [FromQuery] PageRequest paging, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var query = db.CannedReplies.AsNoTracking().Where(r => r.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            var exactShortcut = shortcut.Trim().ToLowerInvariant();
            query = query.Where(r => r.Shortcut == exactShortcut);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(r =>
                r.Shortcut.ToLower().Contains(needle)
                || r.Title.ToLower().Contains(needle)
                || r.Body.ToLower().Contains(needle));
        }

        return await query
            .OrderBy(r => r.Shortcut)
            .Select(r => r.ToResponse())
            .ToPageAsync(paging, Response, cancellationToken);
    }

    [HttpGet("api/canned-replies/{id:guid}")]
    public async Task<ActionResult<CannedReplyResponse>> GetById(Guid id)
    {
        var reply = await db.CannedReplies
            .SingleOrDefaultAsync(r => r.Id == id && r.WorkspaceId == User.GetWorkspaceId());
        return reply is null ? NotFound() : reply.ToResponse();
    }

    [HttpPut("api/canned-replies/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public Task<ActionResult<CannedReplyResponse>> Update(Guid id, UpdateCannedReplyRequest request) =>
        WriteTransactions.RunAsync(db, () => UpdateCore(id, request), HttpContext.RequestAborted);

    private async Task<ActionResult<CannedReplyResponse>> UpdateCore(Guid id, UpdateCannedReplyRequest request)
    {
        var reply = await db.CannedReplies
            .SingleOrDefaultAsync(r => r.Id == id && r.WorkspaceId == User.GetWorkspaceId());
        if (reply is null)
        {
            return NotFound();
        }

        var shortcut = request.Shortcut.Trim().ToLowerInvariant();
        var duplicate = await db.CannedReplies.AnyAsync(r =>
            r.WorkspaceId == reply.WorkspaceId && r.Shortcut == shortcut && r.Id != id);
        if (duplicate)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate shortcut",
                Detail = $"A canned reply with shortcut '{shortcut}' already exists in this workspace.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        var changed = new List<string>();
        if (reply.Shortcut != shortcut) changed.Add("Shortcut");
        if (reply.Title != request.Title) changed.Add("Title");
        if (reply.Body != request.Body) changed.Add("Body");
        ConfigurationAudits.CannedReply(db, reply.WorkspaceId, User.GetTeammateId(), id, ConfigurationAuditAction.Updated, changed, clock.GetUtcNow());
        reply.Shortcut = shortcut;
        reply.Title = request.Title;
        reply.Body = request.Body;
        await db.SaveChangesAsync();

        return reply.ToResponse();
    }

    [HttpDelete("api/canned-replies/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Delete(Guid id) => WriteTransactions.RunAsync(db, () => DeleteCore(id), HttpContext.RequestAborted);

    private async Task<IActionResult> DeleteCore(Guid id)
    {
        var reply = await db.CannedReplies
            .SingleOrDefaultAsync(r => r.Id == id && r.WorkspaceId == User.GetWorkspaceId());
        if (reply is null)
        {
            return NotFound();
        }

        db.CannedReplies.Remove(reply);
        ConfigurationAudits.CannedReply(db, reply.WorkspaceId, User.GetTeammateId(), id, ConfigurationAuditAction.Deleted, ["Shortcut", "Title", "Body"], clock.GetUtcNow());
        await db.SaveChangesAsync();

        return NoContent();
    }
}
