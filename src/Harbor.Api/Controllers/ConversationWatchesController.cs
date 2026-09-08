using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ConversationWatchesController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPut("api/conversations/{id:guid}/watch")]
    public Task<IActionResult> Watch(Guid id, CancellationToken ct) => WriteTransactions.RunAsync<IActionResult>(db, async () =>
    {
        var workspaceId = User.GetWorkspaceId();
        var teammateId = User.GetTeammateId();
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == workspaceId, ct)) return NotFound();
        if (await db.ConversationWatchers.AnyAsync(w => w.ConversationId == id && w.TeammateId == teammateId && w.WorkspaceId == workspaceId, ct)) return NoContent();
        if (await db.ConversationWatchers.CountAsync(w => w.ConversationId == id, ct) >= 200)
            return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Conversation already has 200 watchers" });
        db.ConversationWatchers.Add(new ConversationWatcher { ConversationId = id, TeammateId = teammateId,
            WorkspaceId = workspaceId, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }, ct);

    [HttpDelete("api/conversations/{id:guid}/watch")]
    public async Task<IActionResult> Unwatch(Guid id, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct)) return NotFound();
        await db.ConversationWatchers.Where(w => w.ConversationId == id && w.WorkspaceId == User.GetWorkspaceId()
            && w.TeammateId == User.GetTeammateId()).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/my-notifications")]
    public async Task<ActionResult<List<ReplyNotificationResponse>>> List(Guid workspaceId, [FromQuery] PageRequest paging,
        [FromQuery] bool unreadOnly = true, CancellationToken ct = default) =>
        await db.ReplyNotifications.AsNoTracking().Where(n => n.WorkspaceId == workspaceId && n.TeammateId == User.GetTeammateId()
            && (!unreadOnly || n.ReadAt == null)).OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Select(n => new ReplyNotificationResponse(n.Id, n.ConversationId, n.MessageId, n.CreatedAt, n.ReadAt)).ToPageAsync(paging, Response, ct);

    [HttpPut("api/notifications/{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var own = db.ReplyNotifications.Where(n => n.Id == id && n.WorkspaceId == User.GetWorkspaceId() && n.TeammateId == User.GetTeammateId());
        if (!await own.AnyAsync(ct)) return NotFound();
        var now = clock.GetUtcNow();
        await own.Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return NoContent();
    }
}
