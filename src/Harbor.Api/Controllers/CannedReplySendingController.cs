using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class CannedReplySendingController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private record RenderContext(Conversation Conversation, string Template, CannedReplyValues Values);

    private async Task<RenderContext?> Load(Guid id, Guid replyId, bool trackConversation, CancellationToken ct)
    {
        var workspaceId = User.GetWorkspaceId();
        var query = db.Conversations.Where(c => c.Id == id && c.WorkspaceId == workspaceId);
        var conversation = await (trackConversation ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
        if (conversation is null) return null;
        var reply = await db.CannedReplies.AsNoTracking().SingleOrDefaultAsync(r => r.Id == replyId && r.WorkspaceId == workspaceId, ct);
        if (reply is null) throw new DomainException("The canned reply does not exist in this workspace.");
        var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversation.ContactId && c.WorkspaceId == workspaceId, ct);
        var teammate = await db.Teammates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == User.GetTeammateId() && t.WorkspaceId == workspaceId, ct);
        if (contact is null || teammate is null) throw new DomainException("The rendering context is unavailable.");
        return new RenderContext(conversation, reply.Body, new CannedReplyValues(contact.Name, contact.Email, teammate.Name, conversation.Subject));
    }

    [HttpPost("api/conversations/{id:guid}/canned-replies/{replyId:guid}/preview")]
    public async Task<ActionResult<CannedReplyRenderResult>> Preview(Guid id, Guid replyId, CancellationToken ct)
    {
        var context = await Load(id, replyId, false, ct);
        return context is null ? NotFound() : CannedReplyRenderer.Render(context.Template, context.Values);
    }

    [HttpPost("api/conversations/{id:guid}/canned-replies/{replyId:guid}/send")]
    public async Task<ActionResult<MessageResponse>> Send(Guid id, Guid replyId, CancellationToken ct)
    {
        var context = await Load(id, replyId, true, ct);
        if (context is null) return NotFound();
        var rendered = CannedReplyRenderer.Render(context.Template, context.Values);
        if (string.IsNullOrWhiteSpace(rendered.Body)) throw new DomainException("A sent reply must contain nonblank text.");
        var message = new Message { ConversationId = id, AuthorType = AuthorType.Teammate,
            AuthorTeammateId = User.GetTeammateId(), Kind = MessageKind.Reply, Body = rendered.Body };
        await MessageAppending.StageAsync(db, context.Conversation, message, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return CreatedAtAction("GetById", "Conversations", new { id }, message.ToResponse());
    }
}
