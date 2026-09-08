using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record ClaimNextRequest(Guid? InboxId = null);

[ApiController]
public sealed class ClaimNextController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/conversations/claim-next")]
    public Task<ActionResult<ConversationSummaryResponse>> Claim(Guid workspaceId, ClaimNextRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<ConversationSummaryResponse>>(db, async () =>
        {
            var teammate = await db.Teammates.SingleOrDefaultAsync(t => t.Id == User.GetTeammateId() && t.WorkspaceId == workspaceId, ct);
            if (teammate is null) return NotFound();
            if (request.InboxId is { } inbox && !await db.Inboxes.AnyAsync(i => i.Id == inbox && i.WorkspaceId == workspaceId, ct))
                return UnprocessableEntity(new ProblemDetails { Title = "Unavailable inbox", Status = 422 });
            if (teammate.Availability != TeammateAvailability.Available) return Conflict(new ProblemDetails { Title = "Set your availability before claiming work.", Status = 409 });
            var load = await db.Conversations.CountAsync(c => c.WorkspaceId == workspaceId && c.AssignedTeammateId == teammate.Id && c.State != ConversationState.Closed, ct);
            if (teammate.CapacityLimit is { } capacity && load >= capacity) return Conflict(new ProblemDetails { Title = "Your assigned-work capacity is full.", Status = 409 });
            var candidate = await db.Conversations.Include(c => c.Tags).ThenInclude(t => t.Tag)
                .Where(c => c.WorkspaceId == workspaceId && c.State == ConversationState.Open && c.AssignedTeammateId == null && c.AssignedTeamId == null
                    && (request.InboxId == null || c.InboxId == request.InboxId))
                .OrderByDescending(c => c.Priority).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id).FirstOrDefaultAsync(ct);
            if (candidate is null) return NoContent();
            await ManualAssignments.StageAsync(db, candidate, teammate.Id, teammate.Id, null, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return candidate.ToSummaryResponse();
        }, ct);
}
