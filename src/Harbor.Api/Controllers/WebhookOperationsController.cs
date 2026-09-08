using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public class WebhookOperationsController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("api/webhook-deliveries/{id:guid}/attempts")]
    public async Task<IActionResult> Attempts(Guid id, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        if (!await WebhookQueries.Scoped(db, User.GetWorkspaceId()).AnyAsync(d => d.Id == id, ct)) return NotFound();
        var attempts = await db.WebhookAttempts.AsNoTracking().Where(a => a.DeliveryId == id && a.WorkspaceId == User.GetWorkspaceId())
            .OrderBy(a => a.AttemptNumber).Select(a => new { a.Id, a.AttemptNumber, a.StartedAt, a.FinishedAt,
                a.Outcome, a.ResponseStatusCode, a.FailureCode }).ToPageAsync(paging, Response, ct);
        return Ok(attempts);
    }
    [HttpGet("api/webhook-deliveries/{id:guid}")]
    public async Task<ActionResult<WebhookDeliveryResponse>> Delivery(Guid id, CancellationToken ct)
    {
        var delivery = await WebhookQueries.Scoped(db, User.GetWorkspaceId()).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        return delivery is null ? NotFound() : delivery.ToResponse();
    }

    [HttpPost("api/webhook-deliveries/{id:guid}/redrive")]
    public async Task<ActionResult<WebhookDeliveryResponse>> Redrive(Guid id, CancellationToken ct)
    {
        var workspaceId = User.GetWorkspaceId();
        var source = await WebhookQueries.Scoped(db, workspaceId).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (source is null) return NotFound();
        if (source.OriginalDeliveryId is not null)
            return Conflict(new ProblemDetails { Status = 409, Title = "A redrive child cannot be redriven" });
        var existing = await WebhookQueries.Scoped(db, workspaceId).AsNoTracking().SingleOrDefaultAsync(d => d.OriginalDeliveryId == id, ct);
        if (existing is not null) return existing.ToResponse();
        if (source.Status != WebhookDeliveryStatus.Failed)
            return Conflict(new ProblemDetails { Status = 409, Title = "Only exhausted deliveries can be redriven" });
        if (!await db.WebhookSubscriptions.AnyAsync(s => s.Id == source.SubscriptionId && s.WorkspaceId == workspaceId && s.IsActive, ct))
            return Conflict(new ProblemDetails { Status = 409, Title = "The subscription is paused" });
        var now = clock.GetUtcNow();
        var child = new WebhookDelivery { WorkspaceId = workspaceId, SubscriptionId = source.SubscriptionId,
            EventType = source.EventType, Payload = source.Payload, OriginalDeliveryId = source.Id, CreatedAt = now, NextAttemptAt = now };
        db.WebhookDeliveries.Add(child);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            db.Entry(child).State = EntityState.Detached;
            existing = await WebhookQueries.Scoped(db, workspaceId).AsNoTracking().SingleOrDefaultAsync(d => d.OriginalDeliveryId == id, ct);
            if (existing is null) throw;
            return existing.ToResponse();
        }
        return CreatedAtAction(nameof(Delivery), new { id = child.Id }, child.ToResponse());
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/webhooks/health")]
    public async Task<ActionResult<WebhookHealthResponse>> Health(Guid workspaceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var summary = await WebhookQueries.Scoped(db, workspaceId).AsNoTracking()
            .Select(d => new { d.Status, Active = d.Subscription!.IsActive, d.NextAttemptAt, d.DeliveredAt })
            .GroupBy(d => 1).Select(g => new
            {
                Total = g.Count(), Pending = g.Count(d => d.Status == WebhookDeliveryStatus.Pending),
                Succeeded = g.Count(d => d.Status == WebhookDeliveryStatus.Succeeded), Failed = g.Count(d => d.Status == WebhookDeliveryStatus.Failed),
                Ready = g.Count(d => d.Status == WebhookDeliveryStatus.Pending && d.Active && d.NextAttemptAt <= now),
                Deferred = g.Count(d => d.Status == WebhookDeliveryStatus.Pending && d.Active && d.NextAttemptAt > now),
                Paused = g.Count(d => d.Status == WebhookDeliveryStatus.Pending && !d.Active),
                Oldest = g.Min(d => d.Status == WebhookDeliveryStatus.Pending && d.Active && d.NextAttemptAt <= now ? (DateTimeOffset?)d.NextAttemptAt : null),
                Latest = g.Max(d => d.Status == WebhookDeliveryStatus.Succeeded ? d.DeliveredAt : null),
            }).SingleOrDefaultAsync(ct);
        if (summary is null) return new WebhookHealthResponse(now, 0, 0, 0, 0, 0, 0, 0, null, null, null);
        var terminal = summary.Succeeded + summary.Failed;
        return new WebhookHealthResponse(now, summary.Total, summary.Pending, summary.Succeeded, summary.Failed,
            summary.Ready, summary.Deferred, summary.Paused, summary.Oldest, summary.Latest,
            terminal == 0 ? null : Math.Round((double)summary.Failed / terminal, 4));
    }
}
