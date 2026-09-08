using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class TransfersController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("api/conversations/{id:guid}/transfer")]
    public Task<ActionResult<ConversationSummaryResponse>> Transfer(Guid id, TransferConversationRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync(db, () => TransferCore(id, request, ct), ct);

    private async Task<ActionResult<ConversationSummaryResponse>> TransferCore(Guid id, TransferConversationRequest request, CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct);
        if (conversation is null) return NotFound();
        if (conversation.Channel != MessageChannel.Chat)
            return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Only chat conversations can be transferred" });
        var destination = await db.Inboxes.SingleOrDefaultAsync(i => i.Id == request.DestinationInboxId && i.WorkspaceId == conversation.WorkspaceId, ct);
        if (destination is null) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable destination inbox" });
        InboxLifecycle.RequireActiveForNewWork(destination);
        if (conversation.InboxId == destination.Id) return conversation.ToSummaryResponse();
        var now = clock.GetUtcNow();
        db.ConversationTransferEvents.Add(new ConversationTransferEvent { ConversationId = id,
            ActorTeammateId = User.GetTeammateId(), FromInboxId = conversation.InboxId, ToInboxId = destination.Id, CreatedAt = now });
        conversation.MoveToInbox(destination.Id, now);
        await SlaPolicies.ApplyAsync(db, conversation, destination);
        await SlaBreaches.DetectAsync(db, conversation, now);
        await db.SaveChangesAsync(ct);
        return conversation.ToSummaryResponse();
    }

    [HttpGet("api/conversations/{id:guid}/transfer-events")]
    public async Task<ActionResult<List<TransferEventResponse>>> History(Guid id, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct)) return NotFound();
        return await db.ConversationTransferEvents.AsNoTracking().Where(e => e.ConversationId == id)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new TransferEventResponse(e.Id, e.ConversationId, e.ActorTeammateId, e.FromInboxId, e.ToInboxId, e.CreatedAt))
            .ToPageAsync(paging, Response, ct);
    }
}
