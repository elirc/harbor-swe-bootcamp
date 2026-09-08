using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class TimelineController(HarborDbContext db) : ControllerBase
{
    private sealed record TimelineKey
    {
        public Guid Id { get; init; }
        public int Kind { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
    }

    [HttpGet("api/conversations/{id:guid}/timeline")]
    public async Task<ActionResult<List<TimelineResponse>>> Get(Guid id, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Conversations.AnyAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct)) return NotFound();
        var keys = db.Messages.Where(m => m.ConversationId == id)
            .Select(m => new TimelineKey { Id = m.Id, Kind = 0, OccurredAt = m.CreatedAt })
            .Concat(db.AssignmentEvents.Where(e => e.ConversationId == id)
                .Select(e => new TimelineKey { Id = e.Id, Kind = 1, OccurredAt = e.CreatedAt }))
            .Concat(db.SlaBreachEvents.Where(e => e.ConversationId == id)
                .Select(e => new TimelineKey { Id = e.Id, Kind = 2, OccurredAt = e.BreachedAt }));
        var selected = await keys.OrderByDescending(k => k.OccurredAt).ThenBy(k => k.Kind).ThenBy(k => k.Id)
            .ToPageAsync(paging, Response, ct);
        var messageIds = selected.Where(k => k.Kind == 0).Select(k => k.Id).ToArray();
        var assignmentIds = selected.Where(k => k.Kind == 1).Select(k => k.Id).ToArray();
        var breachIds = selected.Where(k => k.Kind == 2).Select(k => k.Id).ToArray();
        var messages = await db.Messages.AsNoTracking().Where(m => m.ConversationId == id && messageIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.ToResponse(), ct);
        var assignments = await db.AssignmentEvents.AsNoTracking().Where(e => e.ConversationId == id && assignmentIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.ToResponse(), ct);
        var breaches = await db.SlaBreachEvents.AsNoTracking().Where(e => e.ConversationId == id && breachIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.ToResponse(), ct);
        var response = selected.Select(k => new TimelineResponse(k.Id, (TimelineSource)k.Kind, k.OccurredAt,
            k.Kind == 0 ? messages[k.Id] : null, k.Kind == 1 ? assignments[k.Id] : null,
            k.Kind == 2 ? breaches[k.Id] : null)).ToList();
        await transaction.CommitAsync(ct);
        return response;
    }
}
