using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record PreviewSlaRecalculationRequest([Required, MinLength(1), MaxLength(100)] List<Guid> ConversationIds);
public sealed record SlaRecalculationResponse(Guid Id, string Fingerprint, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? CommittedAt, IReadOnlyList<SlaRecalculationItem> Items);

[ApiController, Authorize(Roles = "Admin")]
public sealed class SlaRecalculationsController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/sla/recalculations/preview")]
    public Task<ActionResult<SlaRecalculationResponse>> Preview(Guid workspaceId, PreviewSlaRecalculationRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<SlaRecalculationResponse>>(db, async () =>
        {
            var preview = await SlaRecalculations.BuildAsync(db, workspaceId, request.ConversationIds, ct);
            var now = clock.GetUtcNow();
            var plan = new SlaRecalculationPlan { WorkspaceId = workspaceId, CreatedByTeammateId = User.GetTeammateId(),
                Fingerprint = preview.Fingerprint, ItemsJson = JsonSerializer.Serialize(preview.Items), CreatedAt = now, ExpiresAt = now.AddMinutes(5) };
            db.SlaRecalculationPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            return ResponseFor(plan, preview.Items);
        }, ct);

    [HttpPost("api/sla-recalculations/{id:guid}/commit")]
    public Task<ActionResult<SlaRecalculationResponse>> Commit(Guid id, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<SlaRecalculationResponse>>(db, async () =>
        {
            var plan = await db.SlaRecalculationPlans.SingleOrDefaultAsync(p => p.Id == id && p.WorkspaceId == User.GetWorkspaceId()
                && p.CreatedByTeammateId == User.GetTeammateId(), ct);
            if (plan is null) return NotFound();
            if (plan.ResultJson is not null) return JsonSerializer.Deserialize<SlaRecalculationResponse>(plan.ResultJson)!;
            var now = clock.GetUtcNow();
            if (plan.ExpiresAt <= now) return StatusCode(410);
            if (plan.SchemaVersion != 1) return Conflict();
            var reviewed = JsonSerializer.Deserialize<List<SlaRecalculationItem>>(plan.ItemsJson)!;
            SlaRecalculationPreview fresh;
            try { fresh = await SlaRecalculations.BuildAsync(db, plan.WorkspaceId, reviewed.Select(i => i.ConversationId).ToArray(), ct); }
            catch (DomainException) { return Conflict(new ProblemDetails { Title = "Plan inputs changed; preview again.", Status = 409 }); }
            if (fresh.Fingerprint != plan.Fingerprint) return Conflict(new ProblemDetails { Title = "Plan inputs changed; preview again.", Status = 409 });
            var ids = reviewed.Select(i => i.ConversationId).ToArray();
            var conversations = await db.Conversations.Where(c => c.WorkspaceId == plan.WorkspaceId && ids.Contains(c.Id)).ToListAsync(ct);
            foreach (var item in reviewed)
            {
                var conversation = conversations.Single(c => c.Id == item.ConversationId);
                SlaPolicies.Stamp(conversation, item.Proposed);
                conversation.UpdatedAt = now;
                db.Entry(conversation).Property(c => c.UpdatedAt).IsModified = true;
                await SlaBreaches.DetectAsync(db, conversation, now);
            }
            plan.CommittedAt = now;
            var result = ResponseFor(plan, reviewed);
            plan.ResultJson = JsonSerializer.Serialize(result);
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

    private static SlaRecalculationResponse ResponseFor(SlaRecalculationPlan plan, IReadOnlyList<SlaRecalculationItem> items) =>
        new(plan.Id, plan.Fingerprint, plan.CreatedAt, plan.ExpiresAt, plan.CommittedAt, items);
}
