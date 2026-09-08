using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Harbor.Api.Controllers;

[ApiController]
public class ConversationsController(HarborDbContext db, ConversationQueries queries, TimeProvider clock, IOptions<JsonOptions> json, PrivateFiles files) : ControllerBase
{
    /// <summary>Starts a conversation with the contact's opening message.</summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/conversations")]
    public async Task<ActionResult<ConversationDetailResponse>> Start(
        Guid workspaceId, StartConversationRequest request)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var keys))
            return await WriteTransactions.RunAsync(db, () => StartCore(workspaceId, request), HttpContext.RequestAborted);
        if (keys.Count != 1 || !IngressIdentity.ValidKey(keys[0] ?? ""))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid Idempotency-Key", Detail = "Use 1-128 ASCII letters, digits, hyphens or underscores." });
        var key = keys[0]!;
        var hash = IngressIdentity.Creation(request);
        var teammateId = User.GetTeammateId();
        return await WriteTransactions.RunAsync<ActionResult<ConversationDetailResponse>>(db, async () =>
        {
            var receipt = await db.CreationReceipts.SingleOrDefaultAsync(r => r.WorkspaceId == workspaceId
                && r.TeammateId == teammateId && r.Operation == "Conversation.Start" && r.Key == key);
            if (receipt is not null)
            {
                if (receipt.SchemaVersion != 1 || receipt.RequestHash != hash)
                    return Conflict(new ProblemDetails { Status = 409, Title = "The request key has different input" });
                Response.Headers["Idempotency-Replayed"] = "true";
                return Replay(receipt);
            }
            var result = await StartCore(workspaceId, request);
            if (result.Result is not CreatedAtActionResult { Value: ConversationDetailResponse detail }) return result;
            var body = JsonSerializer.Serialize(detail, json.Value.JsonSerializerOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(body) > 262_144)
                throw new DomainException("The creation response exceeds the retained-response limit.");
            var now = clock.GetUtcNow();
            receipt = new CreationReceipt { WorkspaceId = workspaceId, TeammateId = teammateId, Key = key,
                RequestHash = hash, ResponseJson = body, Location = Url.Action(nameof(GetById), new { id = detail.Id })!,
                CreatedAt = now, RetainUntil = now.AddHours(24) };
            db.CreationReceipts.Add(receipt);
            await db.SaveChangesAsync(HttpContext.RequestAborted);
            return Replay(receipt);
        }, HttpContext.RequestAborted);
    }

    private ContentResult Replay(CreationReceipt receipt)
    {
        Response.Headers.Location = receipt.Location;
        return new ContentResult { StatusCode = receipt.ResponseStatus, ContentType = receipt.ContentType, Content = receipt.ResponseJson };
    }

    private async Task<ActionResult<ConversationDetailResponse>> StartCore(Guid workspaceId, StartConversationRequest request)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var inbox = await db.Inboxes
            .SingleOrDefaultAsync(i => i.Id == request.InboxId && i.WorkspaceId == workspaceId);
        if (inbox is null)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unknown inbox",
                Detail = "The inbox does not exist in this workspace.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var contact = await db.Contacts
            .SingleOrDefaultAsync(c => c.Id == request.ContactId && c.WorkspaceId == workspaceId);
        if (contact is null)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unknown contact",
                Detail = "The contact does not exist in this workspace.",
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }

        var conversation = await ConversationStarter.StartAsync(
            db, inbox, contact, request.Subject, request.Body, clock.GetUtcNow(),
            request.Priority ?? ConversationPriority.Normal);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = conversation.Id }, conversation.ToDetailResponse());
    }

    /// <summary>Lists conversations with optional filters and full-text-ish search.</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/conversations")]
    public async Task<ActionResult<List<ConversationSummaryResponse>>> List(
        Guid workspaceId, [FromQuery] ConversationFilterRequest filter, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
        {
            return NotFound();
        }

        var query = await queries.BuildAsync(workspaceId, filter, DateTimeOffset.UtcNow, cancellationToken);
        if (query is null)
        {
            return Problem422("Unknown segment", "The segment does not exist in this workspace.");
        }

        var conversations = await ConversationQueries.Order(query, filter.Sort)
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .ToPageAsync(filter, Response, cancellationToken);

        return conversations.Select(c => c.ToSummaryResponse()).ToList();
    }

    /// <summary>Counts the entire filtered queue, regardless of page/pageSize.</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/conversations/overview")]
    public async Task<ActionResult<ConversationOverviewResponse>> Overview(
        Guid workspaceId, [FromQuery] ConversationFilterRequest filter, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
        {
            return NotFound();
        }

        var query = await queries.BuildAsync(workspaceId, filter, DateTimeOffset.UtcNow, cancellationToken);
        if (query is null)
        {
            return Problem422("Unknown segment", "The segment does not exist in this workspace.");
        }

        // One aggregate statement keeps the counts mutually consistent within this read.
        // No entities, messages, or tags need to be loaded for a queue summary.
        var overview = await query.GroupBy(c => 1)
            .Select(g => new ConversationOverviewResponse(
                g.Count(),
                g.Count(c => c.State == ConversationState.Open),
                g.Count(c => c.State == ConversationState.Snoozed),
                g.Count(c => c.State == ConversationState.Closed),
                g.Count(c => c.AssignedTeammateId == null && c.AssignedTeamId == null)))
            .SingleOrDefaultAsync(cancellationToken);

        return overview ?? new ConversationOverviewResponse(0, 0, 0, 0, 0);
    }

    [HttpGet("api/conversations/{id:guid}")]
    public async Task<ActionResult<ConversationDetailResponse>> GetById(Guid id)
    {
        var conversation = await db.Conversations
            .Include(c => c.Messages)
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());

        return conversation is null ? NotFound() : conversation.ToDetailResponse();
    }

    [HttpGet("api/conversations/{id:guid}/messages")]
    public async Task<ActionResult<List<MessageResponse>>> ListMessages(
        Guid id, [FromQuery] MessageFilterRequest filter, CancellationToken cancellationToken)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), cancellationToken))
            return NotFound();
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == id);
        if (filter.Kind is { } kind) query = query.Where(m => m.Kind == kind);
        return await query.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => m.ToResponse()).ToPageAsync(filter, Response, cancellationToken);
    }

    [HttpGet("api/conversations/{id:guid}/message-counts")]
    public async Task<ActionResult<MessageCountsResponse>> MessageCounts(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), cancellationToken))
            return NotFound();
        var counts = await db.Messages.AsNoTracking().Where(m => m.ConversationId == id)
            .GroupBy(m => 1).Select(g => new MessageCountsResponse(
                g.Count(), g.Count(m => m.Kind == MessageKind.Reply), g.Count(m => m.Kind == MessageKind.Note)))
            .SingleOrDefaultAsync(cancellationToken);
        return counts ?? new MessageCountsResponse(0, 0, 0);
    }

    /// <summary>Adds a contact/teammate reply or an internal note to the thread.</summary>
    [HttpPost("api/conversations/{id:guid}/messages")]
    public Task<ActionResult<MessageResponse>> AddMessage(Guid id, AddMessageRequest request) =>
        WriteTransactions.RunAsync(db, () => AddMessageCore(id, request), HttpContext.RequestAborted);

    private async Task<ActionResult<MessageResponse>> AddMessageCore(Guid id, AddMessageRequest request)
    {
        var conversation = await db.Conversations
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (conversation is null)
        {
            return NotFound();
        }

        if (request.Kind == MessageKind.Note && request.AuthorType != AuthorType.Teammate)
        {
            return Problem422("Invalid note author", "Internal notes can only be written by teammates.");
        }

        var message = new Message
        {
            ConversationId = conversation.Id,
            Kind = request.Kind,
            AuthorType = request.AuthorType,
            Body = request.Body,
        };

        var now = DateTimeOffset.UtcNow;
        if (request.AuthorType == AuthorType.Contact)
        {
            if (request.AuthorId != conversation.ContactId)
            {
                return Problem422(
                    "Invalid contact author",
                    "Only the conversation's contact can write contact messages.");
            }

            message.AuthorContactId = request.AuthorId;
            var contact = await db.Contacts.SingleAsync(c => c.Id == conversation.ContactId);
            contact.LastSeenAt = now;
        }
        else
        {
            var teammate = await db.Teammates.FindAsync(request.AuthorId);
            if (teammate is null || teammate.WorkspaceId != conversation.WorkspaceId)
            {
                return Problem422(
                    "Unknown teammate",
                    "The author does not exist in this conversation's workspace.");
            }

            message.AuthorTeammateId = request.AuthorId;
        }

        if (request.AttachmentIds is { Count: > 0 } ids)
        {
            if (conversation.Channel != MessageChannel.Chat || ids.Count > 3 || ids.Distinct().Count() != ids.Count)
                return Problem422("Invalid attachments", "Use up to three distinct uploads on a chat conversation.");
            var attachments = await db.MessageAttachments.Where(a => ids.Contains(a.Id) && a.WorkspaceId == conversation.WorkspaceId
                && a.ConversationId == id && a.UploadedByTeammateId == User.GetTeammateId()
                && a.State == AttachmentState.Uploaded && a.MessageId == null && a.ExpiresAt > clock.GetUtcNow()).ToListAsync();
            if (attachments.Count != ids.Count || attachments.Any(a => !files.Exists(a.WorkspaceId, ArtifactKind.Attachments, a.StorageId)))
                return Problem422("Unavailable attachments", "Uploads must be current, owned by you, and staged for this conversation.");
            foreach (var attachment in attachments)
            { attachment.State = AttachmentState.Attached; attachment.MessageId = message.Id; message.Attachments.Add(attachment); }
        }
        await MessageAppending.StageAsync(db, conversation, message, now);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = conversation.Id }, message.ToResponse());
    }

    /// <summary>Opens, snoozes, or closes the conversation.</summary>
    [HttpPost("api/conversations/{id:guid}/state")]
    public Task<ActionResult<ConversationSummaryResponse>> ChangeState(Guid id, ChangeStateRequest request) =>
        WriteTransactions.RunAsync(db, () => ChangeStateCore(id, request), HttpContext.RequestAborted);

    private async Task<ActionResult<ConversationSummaryResponse>> ChangeStateCore(Guid id, ChangeStateRequest request)
    {
        var conversation = await db.Conversations
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (conversation is null)
        {
            return NotFound();
        }

        // DomainException (e.g. snoozing into the past) is translated to a
        // 422 ProblemDetails response by DomainExceptionHandler.
        var now = DateTimeOffset.UtcNow;
        var previousState = conversation.State;
        switch (request.State)
        {
            case ConversationState.Open:
                conversation.Open(now);
                break;
            case ConversationState.Snoozed when request.SnoozedUntil is { } until:
                conversation.Snooze(until, now);
                break;
            case ConversationState.Snoozed:
                return Problem422("Missing snooze time", "Snoozing requires 'snoozedUntil'.");
            case ConversationState.Closed:
                var blockers = await db.ConversationChecklistItems.Where(i => i.ConversationId == id && i.WorkspaceId == conversation.WorkspaceId
                    && i.Required && i.CompletedAt == null).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync();
                if (blockers.Count > 0) return UnprocessableEntity(new { title = "Complete required checklist items before closing.", status = 422, incompleteRequiredItemIds = blockers });
                conversation.Close(now);
                break;
            default:
                return Problem422("Unknown state", $"Unsupported conversation state '{request.State}'.");
        }

        WorkspaceEvents.State(db, conversation, previousState, now);
        // A close that lands after the resolution target breaches it on the spot.
        await SlaBreaches.DetectAsync(db, conversation, now);
        if (request.State == ConversationState.Closed)
        {
            await Webhooks.PublishAsync(
                db, conversation.WorkspaceId, WebhookEventType.ConversationClosed,
                conversation.ToSummaryResponse(), now);
        }

        await db.SaveChangesAsync();
        return conversation.ToSummaryResponse();
    }

    /// <summary>
    /// Changes priority and re-stamps SLA targets from the policy that now
    /// governs the conversation. The clock still runs from creation, so
    /// escalating an old conversation can put it immediately past due.
    /// </summary>
    [HttpPut("api/conversations/{id:guid}/priority")]
    public async Task<ActionResult<ConversationSummaryResponse>> SetPriority(
        Guid id, SetPriorityRequest request)
    {
        var conversation = await db.Conversations
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (conversation is null)
        {
            return NotFound();
        }

        var inbox = await db.Inboxes.SingleAsync(i => i.Id == conversation.InboxId);
        var now = DateTimeOffset.UtcNow;

        await PriorityChanges.ApplyAsync(db, conversation, inbox, request.Priority, now);
        await db.SaveChangesAsync();

        return conversation.ToSummaryResponse();
    }

    /// <summary>
    /// Records breaches for conversations sitting past a target. The reply and
    /// close paths catch breaches as they happen; this catches the ones where
    /// nothing happens at all, and is safe to call repeatedly.
    /// </summary>
    [HttpPost("api/workspaces/{workspaceId:guid}/sla/evaluate")]
    public async Task<ActionResult<List<SlaBreachEventResponse>>> EvaluateSla(Guid workspaceId)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var candidates = await db.Conversations
            .Where(c => c.WorkspaceId == workspaceId
                && (c.FirstResponseDueAt != null || c.ResolutionDueAt != null))
            .ToListAsync();

        var recorded = await SlaBreaches.DetectAsync(db, candidates, now);
        await db.SaveChangesAsync();

        return recorded.Select(b => b.ToResponse()).ToList();
    }

    /// <summary>Every SLA target this conversation has missed.</summary>
    [HttpGet("api/conversations/{id:guid}/sla-breaches")]
    public async Task<ActionResult<List<SlaBreachEventResponse>>> ListSlaBreaches(
        Guid id, [FromQuery] PageRequest paging)
    {
        var exists = await db.Conversations
            .AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (!exists)
        {
            return NotFound();
        }

        return await db.SlaBreachEvents
            .Where(b => b.ConversationId == id)
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.ToResponse())
            .ToPageAsync(paging, Response);
    }

    /// <summary>Assigns the conversation to a teammate or a team, or unassigns it.</summary>
    [HttpPost("api/conversations/{id:guid}/assignment")]
    public async Task<ActionResult<ConversationSummaryResponse>> Assign(Guid id, AssignConversationRequest request)
    {
        if (request.TeammateId is not null && request.TeamId is not null)
        {
            return Problem422(
                "Ambiguous assignment",
                "Provide only one of 'teammateId' or 'teamId', or neither to unassign.");
        }

        var conversation = await db.Conversations
            .Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (conversation is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;

        if (request.TeammateId is { } teammateId)
        {
            var teammate = await db.Teammates.FindAsync(teammateId);
            if (teammate is null || teammate.WorkspaceId != conversation.WorkspaceId)
            {
                return Problem422("Unknown teammate", "The teammate does not exist in this workspace.");
            }

        }
        else if (request.TeamId is { } teamId)
        {
            var team = await db.Teams.FindAsync(teamId);
            if (team is null || team.WorkspaceId != conversation.WorkspaceId)
            {
                return Problem422("Unknown team", "The team does not exist in this workspace.");
            }

        }
        await ManualAssignments.StageAsync(db, conversation, User.GetTeammateId(), request.TeammateId, request.TeamId, now);

        await db.SaveChangesAsync();
        return conversation.ToSummaryResponse();
    }

    /// <summary>Audit trail of every assignment change, oldest first.</summary>
    [HttpGet("api/conversations/{id:guid}/assignment-events")]
    public async Task<ActionResult<List<AssignmentEventResponse>>> ListAssignmentEvents(
        Guid id, [FromQuery] PageRequest paging)
    {
        var exists = await db.Conversations
            .AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (!exists)
        {
            return NotFound();
        }

        return await db.AssignmentEvents
            .Where(a => a.ConversationId == id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.ToResponse())
            .ToPageAsync(paging, Response);
    }

    private ObjectResult Problem422(string title, string detail) =>
        UnprocessableEntity(new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = StatusCodes.Status422UnprocessableEntity,
        });
}
