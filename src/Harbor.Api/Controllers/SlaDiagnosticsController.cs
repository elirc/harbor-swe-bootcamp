using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class SlaDiagnosticsController(HarborDbContext db, ConversationQueries queries, TimeProvider clock) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/sla/simulate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SlaSimulationResponse>> Simulate(Guid workspaceId, SimulateSlaRequest request, CancellationToken ct)
    {
        var inbox = await db.Inboxes.AsNoTracking().SingleOrDefaultAsync(i => i.Id == request.InboxId && i.WorkspaceId == workspaceId, ct);
        if (inbox is null) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable inbox" });
        var policies = await db.SlaPolicies.AsNoTracking().Where(p => p.WorkspaceId == workspaceId).ToListAsync(ct);
        var calculated = await SlaPolicies.CalculateAsync(db, policies, inbox, request.Priority!.Value, request.CreatedAt!.Value);
        var targets = calculated.Description;
        return new SlaSimulationResponse(targets.Source, targets.PolicyId, targets.PolicyName,
            targets.FirstResponseMinutes, targets.ResolutionMinutes, calculated.FirstResponseDueAt, calculated.ResolutionDueAt, targets.WorkingCalendarId);
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/sla/upcoming")]
    public async Task<ActionResult<List<UpcomingTargetResponse>>> Upcoming(Guid workspaceId, [FromQuery] UpcomingSlaRequest filter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var before = now.AddMinutes(filter.WithinMinutes);
        var query = await queries.BuildAsync(workspaceId, filter, now, ct);
        if (query is null) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unknown segment" });
        query = query.Where(c => c.State != ConversationState.Closed);
        var targets = query.Where(c => c.FirstRespondedAt == null && c.FirstResponseDueAt >= now && c.FirstResponseDueAt < before)
            .Select(c => new UpcomingTargetResponse { ConversationId = c.Id, Subject = c.Subject, InboxId = c.InboxId,
                Kind = SlaBreachKind.FirstResponse, DueAt = c.FirstResponseDueAt!.Value })
            .Concat(query.Where(c => c.FirstResolvedAt == null && c.ResolutionDueAt >= now && c.ResolutionDueAt < before)
                .Select(c => new UpcomingTargetResponse { ConversationId = c.Id, Subject = c.Subject, InboxId = c.InboxId,
                    Kind = SlaBreachKind.Resolution, DueAt = c.ResolutionDueAt!.Value }));
        Response.Headers["X-Evaluated-At"] = now.ToString("O");
        return await targets.OrderBy(t => t.DueAt).ThenBy(t => t.ConversationId).ThenBy(t => t.Kind)
            .ToPageAsync(filter, Response, ct);
    }
}
