using System.Text.Json;
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
[Authorize(Roles = "Admin")]
public class ContactMergesController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpPost("api/workspaces/{workspaceId:guid}/contact-merges/preview")]
    public Task<ActionResult<ContactMergePlanResponse>> Preview(Guid workspaceId, PreviewContactMergeRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<ContactMergePlanResponse>>(db, async () =>
        {
            var preview = await ContactMergePreview.BuildAsync(db, workspaceId, request.SourceId!.Value, request.TargetId!.Value, ct);
            var now = clock.GetUtcNow();
            var plan = new ContactMergePlan { WorkspaceId = workspaceId, CreatedByTeammateId = User.GetTeammateId(),
                SourceId = preview.SourceId, TargetId = preview.TargetId, Fingerprint = preview.Fingerprint,
                PreviewJson = JsonSerializer.Serialize(preview, Json), CreatedAt = now, ExpiresAt = now.AddMinutes(10) };
            if (plan.PreviewJson.Length > 100_000) throw new DomainException("The preview exceeds the retained plan limit.");
            db.ContactMergePlans.Add(plan);
            await db.SaveChangesAsync(ct);
            return new ContactMergePlanResponse(plan.Id, plan.ExpiresAt, preview);
        }, ct);

    [HttpPost("api/contact-merges/{id:guid}/commit")]
    public Task<ActionResult<ContactMergeResult>> Commit(Guid id, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<ContactMergeResult>>(db, async () =>
        {
            var plan = await db.ContactMergePlans.SingleOrDefaultAsync(p => p.Id == id && p.WorkspaceId == User.GetWorkspaceId(), ct);
            if (plan is null) return NotFound();
            if (plan.ResultJson is not null) return JsonSerializer.Deserialize<ContactMergeResult>(plan.ResultJson, Json)!;
            var now = clock.GetUtcNow();
            if (now >= plan.ExpiresAt) return StatusCode(410, new ProblemDetails { Status = 410, Title = "Merge plan expired" });
            if (plan.SchemaVersion != 1) return Stale();
            MergePreview fresh;
            try { fresh = await ContactMergePreview.BuildAsync(db, plan.WorkspaceId, plan.SourceId, plan.TargetId, ct); }
            catch (DomainException) { return Stale(); }
            if (fresh.Fingerprint != plan.Fingerprint) return Stale();
            var source = await db.Contacts.SingleAsync(c => c.Id == plan.SourceId && c.WorkspaceId == plan.WorkspaceId, ct);
            var target = await db.Contacts.SingleAsync(c => c.Id == plan.TargetId && c.WorkspaceId == plan.WorkspaceId, ct);
            var conversations = await db.Conversations.Where(c => c.ContactId == source.Id && c.WorkspaceId == plan.WorkspaceId).ToListAsync(ct);
            var messages = await db.Messages.Where(m => m.AuthorContactId == source.Id && m.Conversation!.WorkspaceId == plan.WorkspaceId).ToListAsync(ct);
            foreach (var conversation in conversations) conversation.ContactId = target.Id;
            foreach (var message in messages) message.AuthorContactId = target.Id;
            target.ExternalId = fresh.TargetFields.ExternalId;
            target.AttributesJson = ContactAttributes.Write(fresh.TargetFields.Attributes);
            target.LastSeenAt = fresh.TargetFields.LastSeenAt;
            db.ChangeTracker.DetectChanges();
            db.Contacts.Remove(source);
            var result = new ContactMergeResult(id, source.Id, target.Id, conversations.Count, messages.Count, now);
            plan.ResultJson = JsonSerializer.Serialize(result, Json);
            plan.CommittedAt = now;
            plan.CommittedByTeammateId = User.GetTeammateId();
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);

    private ObjectResult Stale() => Conflict(new ProblemDetails { Status = 409, Title = "Merge plan is stale", Detail = "Create and review a new preview." });
}
