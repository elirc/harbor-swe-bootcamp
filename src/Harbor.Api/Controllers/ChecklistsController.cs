using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record CreateChecklistItemRequest([Required, MaxLength(200)] string Title, [Required] bool? Required);
public sealed record EditChecklistItemRequest([Required, MaxLength(200)] string Title, [Required] bool? Required, [Required, NonEmptyGuid] Guid? ExpectedVersion);
public sealed record CompleteChecklistItemRequest([Required] bool? Completed, [Required, NonEmptyGuid] Guid? ExpectedVersion);
public sealed record DeleteChecklistItemRequest([Required, NonEmptyGuid] Guid? ExpectedVersion);

[ApiController]
public sealed class ChecklistsController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("api/conversations/{id:guid}/checklist-items")]
    public async Task<ActionResult<List<ConversationChecklistItem>>> List(Guid id, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct)) return NotFound();
        return await db.ConversationChecklistItems.AsNoTracking().Where(i => i.ConversationId == id && i.WorkspaceId == User.GetWorkspaceId())
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(ct);
    }

    [HttpPost("api/conversations/{id:guid}/checklist-items")]
    public Task<ActionResult<ConversationChecklistItem>> Create(Guid id, CreateChecklistItemRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<ConversationChecklistItem>>(db, async () =>
        {
            var parent = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct);
            if (parent is null) return NotFound();
            if (parent.State == ConversationState.Closed) return Conflict();
            var title = request.Title.Trim();
            if (title.Length == 0) return BadRequest();
            if (await db.ConversationChecklistItems.CountAsync(i => i.ConversationId == id, ct) >= 50) return UnprocessableEntity();
            var item = new ConversationChecklistItem { ConversationId = id, WorkspaceId = parent.WorkspaceId, Title = title,
                Required = request.Required!.Value, CreatedAt = clock.GetUtcNow() };
            db.ConversationChecklistItems.Add(item);
            Touch(parent);
            await db.SaveChangesAsync(ct);
            return Created($"/api/conversations/{id}/checklist-items", item);
        }, ct);

    [HttpPut("api/checklist-items/{id:guid}")]
    public Task<IActionResult> Edit(Guid id, EditChecklistItemRequest request, CancellationToken ct) =>
        Mutate(id, request.ExpectedVersion!.Value, (item, parent) =>
        {
            var title = request.Title.Trim();
            if (title.Length == 0) return BadRequest();
            if (item.Title != title || item.Required != request.Required!.Value)
            { item.Title = title; item.Required = request.Required!.Value; Touch(parent); }
            return Ok(item);
        }, ct);

    [HttpPut("api/checklist-items/{id:guid}/completion")]
    public Task<IActionResult> Complete(Guid id, CompleteChecklistItemRequest request, CancellationToken ct) =>
        Mutate(id, request.ExpectedVersion!.Value, (item, parent) =>
        {
            if (request.Completed!.Value != (item.CompletedAt is not null))
            {
                item.CompletedAt = request.Completed.Value ? clock.GetUtcNow() : null;
                item.CompletedByTeammateId = request.Completed.Value ? User.GetTeammateId() : null;
                Touch(parent);
            }
            return Ok(item);
        }, ct);

    [HttpDelete("api/checklist-items/{id:guid}")]
    public Task<IActionResult> Delete(Guid id, DeleteChecklistItemRequest request, CancellationToken ct) =>
        Mutate(id, request.ExpectedVersion!.Value, (item, parent) =>
        { db.ConversationChecklistItems.Remove(item); Touch(parent); return NoContent(); }, ct);

    private Task<IActionResult> Mutate(Guid id, Guid expected, Func<ConversationChecklistItem, Conversation, IActionResult> change, CancellationToken ct) =>
        WriteTransactions.RunAsync<IActionResult>(db, async () =>
        {
            var item = await db.ConversationChecklistItems.SingleOrDefaultAsync(i => i.Id == id && i.WorkspaceId == User.GetWorkspaceId(), ct);
            if (item is null) return NotFound();
            var parent = await db.Conversations.SingleAsync(c => c.Id == item.ConversationId && c.WorkspaceId == item.WorkspaceId, ct);
            if (parent.State == ConversationState.Closed || item.Version != expected) return Conflict();
            var result = change(item, parent);
            if (result is BadRequestResult) return result;
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

    private void Touch(Conversation parent)
    {
        parent.UpdatedAt = clock.GetUtcNow();
        // A fixed clock can return the old value; still force a parent token change.
        db.Entry(parent).Property(c => c.UpdatedAt).IsModified = true;
    }
}
