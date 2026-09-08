using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

/// <summary>
/// The email channel: ingestion of parsed inbound mail, and rendering of
/// outbound replies.
///
/// Harbor does not parse MIME. A mail provider does that and posts the
/// extracted fields here, authenticating with an API key like any other
/// client — which keeps ingestion inside the same workspace-scoped
/// authorization as everything else.
/// </summary>
[ApiController]
public class EmailController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Ingests one inbound email: routes it to an inbox by its To address,
    /// resolves the sender to a contact, and either threads it onto an
    /// existing conversation or starts a new one.
    /// </summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/email/inbound")]
    public async Task<ActionResult<InboundEmailResponse>> Inbound(
        Guid workspaceId, InboundEmailRequest request)
        => await WriteTransactions.RunAsync(db, () => Ingest(workspaceId, request), HttpContext.RequestAborted);

    private async Task<ActionResult<InboundEmailResponse>> Ingest(Guid workspaceId, InboundEmailRequest request)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var to = Normalize(request.To);
        var inbox = await db.Inboxes
            .SingleOrDefaultAsync(i => i.WorkspaceId == workspaceId && i.EmailAddress == to);
        if (inbox is null)
        {
            return Problem422(
                "Unknown inbox address",
                $"No inbox in this workspace receives mail at '{to}'.");
        }

        var messageId = IngressIdentity.Identifier(request.MessageId);
        var hash = messageId is null ? null : IngressIdentity.Email(request);
        if (messageId is not null)
        {
            var old = await db.InboundEmailReceipts.AsNoTracking().SingleOrDefaultAsync(r =>
                r.WorkspaceId == workspaceId && r.InboxId == inbox.Id && r.MessageId == messageId);
            if (old is not null)
            {
                if (old.SchemaVersion != 1 || old.RequestHash != hash)
                    return Conflict(new ProblemDetails { Status = 409, Title = "Message-ID has different input" });
                ReceiptHeaders(old, true);
                return Result(old);
            }
        }

        var now = clock.GetUtcNow();
        var from = Normalize(request.From);
        var existing = await FindThreadAsync(workspaceId, inbox.Id, request);
        Contact? contact;
        if (existing is not null)
        {
            contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == existing.ContactId && c.WorkspaceId == workspaceId);
            if (existing.InboxId != inbox.Id || contact?.Email is null || Normalize(contact.Email) != from)
                return Problem422("Invalid thread reference", "The referenced conversation must belong to this inbox and sender.");
        }
        else
        {
            InboxLifecycle.RequireActiveForNewWork(inbox);
            var whitespace = QueryText.WhitespaceCharacters;
            contact = await db.Contacts.Where(c => c.WorkspaceId == workspaceId && c.Email != null
                && HarborDbContext.TrimCharacters(c.Email, whitespace)!.ToLower() == from).OrderBy(c => c.Id).FirstOrDefaultAsync();
        }

        // The sender may be writing in for the first time; an email address is
        // the only identity an inbound mail carries.
        var createdContact = contact is null;
        if (contact is null)
        {
            contact = new Contact
            {
                WorkspaceId = workspaceId,
                Name = string.IsNullOrWhiteSpace(request.FromName) ? from : request.FromName.Trim(),
                Email = from,
                CreatedAt = now,
            };
            db.Contacts.Add(contact);
        }

        InboundEmailResponse response;
        if (existing is not null)
        {
            var reply = new Message
            {
                ConversationId = existing.Id,
                Kind = MessageKind.Reply,
                AuthorType = AuthorType.Contact,
                AuthorContactId = existing.ContactId,
                Body = request.Body,
                Channel = MessageChannel.Email,
                EmailMessageId = messageId,
                CreatedAt = now,
            };

            // Goes through the same domain path as a chat reply, so an emailed
            // reply reopens a closed conversation exactly like any other.
            contact.LastSeenAt = now;
            await MessageAppending.StageAsync(db, existing, reply, now, preserveInboundEmailIdentity: true);
            response = new InboundEmailResponse(existing.Id, reply.Id, contact.Id, false, createdContact);
        }
        else
        {
            var conversation = await ConversationStarter.StartAsync(
                db, inbox, contact, request.Subject, request.Body, now,
                channel: MessageChannel.Email, emailMessageId: messageId);
            response = new InboundEmailResponse(conversation.Id, conversation.Messages.Single().Id, contact.Id, true, createdContact);
        }
        if (messageId is not null)
        {
            var receipt = new InboundEmailReceipt { WorkspaceId = workspaceId, InboxId = inbox.Id, MessageId = messageId,
                RequestHash = hash!, ConversationId = response.ConversationId, MessageRecordId = response.MessageId,
                ContactId = response.ContactId, CreatedConversation = response.StartedNewConversation,
                CreatedContact = response.CreatedContact, IngestedAt = now };
            db.InboundEmailReceipts.Add(receipt);
            ReceiptHeaders(receipt, false);
        }
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return response;
    }

    private void ReceiptHeaders(InboundEmailReceipt receipt, bool replayed)
    {
        Response.Headers["Inbound-Receipt-Id"] = receipt.Id.ToString();
        Response.Headers["Inbound-Replayed"] = replayed ? "true" : "false";
    }

    private static InboundEmailResponse Result(InboundEmailReceipt receipt) => new(receipt.ConversationId,
        receipt.MessageRecordId, receipt.ContactId, receipt.CreatedConversation, receipt.CreatedContact);

    [HttpGet("api/inbound-email-receipts/{id:guid}")]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
    {
        var receipt = await db.InboundEmailReceipts.AsNoTracking().Where(r => r.Id == id && r.WorkspaceId == User.GetWorkspaceId())
            .Select(r => new { r.Id, r.InboxId, r.ConversationId, MessageId = r.MessageRecordId, r.ContactId,
                r.CreatedConversation, r.CreatedContact, r.IngestedAt }).SingleOrDefaultAsync(ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    /// <summary>
    /// Renders a teammate reply as the email that would be sent to the
    /// contact, including the headers that keep it in their mail thread.
    /// </summary>
    [HttpGet("api/messages/{id:guid}/email")]
    public async Task<ActionResult<RenderedEmailResponse>> Render(Guid id)
    {
        var message = await db.Messages
            .Include(m => m.Conversation)
            .SingleOrDefaultAsync(m => m.Id == id
                && m.Conversation!.WorkspaceId == User.GetWorkspaceId());
        if (message?.Conversation is not { } conversation)
        {
            return NotFound();
        }

        var inbox = await db.Inboxes.SingleAsync(i => i.Id == conversation.InboxId);
        var contact = await db.Contacts.SingleAsync(c => c.Id == conversation.ContactId);
        var thread = await db.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .ToListAsync();

        // DomainException (no inbox address, no contact address, a note, or a
        // contact's own message) becomes a 422 via DomainExceptionHandler.
        var rendered = EmailRendering.Render(conversation, inbox, contact, message, thread);
        return rendered.ToResponse(message.Id);
    }

    /// <summary>
    /// Finds the conversation an inbound mail belongs to by matching its
    /// In-Reply-To or References against Message-IDs we have already seen.
    /// References is walked newest-first: the nearest known ancestor is the
    /// best evidence of which thread this is.
    /// </summary>
    private async Task<Conversation?> FindThreadAsync(Guid workspaceId, Guid inboxId, InboundEmailRequest request)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.InReplyTo))
        {
            candidates.Add(request.InReplyTo.Trim());
        }

        if (request.References is { Count: > 0 } references)
        {
            candidates.AddRange(references
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Reverse());
        }

        foreach (var candidate in candidates.Distinct())
        {
            var match = await db.Messages
                .Include(m => m.Conversation)
                .Where(m => m.EmailMessageId == candidate && m.Conversation!.WorkspaceId == workspaceId)
                .OrderBy(m => m.Conversation!.InboxId != inboxId).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id)
                .FirstOrDefaultAsync();
            if (match?.Conversation is not null)
            {
                return match.Conversation;
            }
        }

        return null;
    }

    /// <summary>Addresses are matched case-insensitively.</summary>
    private static string Normalize(string address) => address.Trim().ToLowerInvariant();

    private ObjectResult Problem422(string title, string detail) =>
        UnprocessableEntity(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status422UnprocessableEntity,
        });
}
