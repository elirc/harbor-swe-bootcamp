using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record ConversationExportResponse(Guid Id, Guid WorkspaceId, Guid RequestedByTeammateId,
    ConversationExportStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? SnapshotAt, DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt, int AttemptCount, int? RowCount, long? ByteLength, string? Sha256, string? FailureCode);

[ApiController, Authorize(Roles = "Admin")]
public sealed class ConversationExportsController(HarborDbContext db, PrivateFiles files, TimeProvider clock, ILogger<ConversationExportsController> logger) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/conversation-exports"), RequestSizeLimit(32 * 1024)]
    public Task<ActionResult<ConversationExportResponse>> Enqueue(Guid workspaceId, [FromBody] JsonElement request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<ConversationExportResponse>>(db, async () =>
        {
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
            if (request.ValueKind != JsonValueKind.Object || request.GetRawText().Length > 20_000) return BadRequest();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in request.EnumerateObject())
                if (!names.Add(property.Name) || property.Name.Equals("page", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("sort", StringComparison.OrdinalIgnoreCase)) return BadRequest();
            ConversationFilterRequest filter;
            try { filter = request.Deserialize<ConversationFilterRequest>(ConversationExports.Json)!; }
            catch (JsonException) { return BadRequest(); }
            if (!TryValidateModel(filter)) return ValidationProblem(ModelState);
            if (filter.InboxId is { } inbox && !await db.Inboxes.AnyAsync(i => i.Id == inbox && i.WorkspaceId == workspaceId, ct)
                || filter.ContactId is { } contact && !await db.Contacts.AnyAsync(c => c.Id == contact && c.WorkspaceId == workspaceId, ct)
                || filter.AssignedTeammateId is { } teammate && !await db.Teammates.AnyAsync(t => t.Id == teammate && t.WorkspaceId == workspaceId, ct)
                || filter.AssignedTeamId is { } team && !await db.Teams.AnyAsync(t => t.Id == team && t.WorkspaceId == workspaceId, ct)) return UnprocessableEntity();
            string? capturedRules = null;
            if (filter.SegmentId is { } segmentId)
            {
                var segment = await db.Segments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == segmentId && s.WorkspaceId == workspaceId, ct);
                if (segment is null) return UnprocessableEntity();
                capturedRules = segment.RulesJson;
            }
            var frozen = JsonSerializer.SerializeToNode(filter, ConversationExports.Json)!.AsObject();
            foreach (var property in new[] { "page", "pageSize", "sort", "segmentId" }) frozen.Remove(property);
            var filterJson = frozen.ToJsonString();
            if (filterJson.Length > 20_000) return BadRequest();
            var job = new ConversationExportJob { WorkspaceId = workspaceId, RequestedByTeammateId = User.GetTeammateId(),
                FilterJson = filterJson, CapturedSegmentRulesJson = capturedRules, CreatedAt = clock.GetUtcNow() };
            db.ConversationExportJobs.Add(job); await db.SaveChangesAsync(ct);
            return Accepted($"/api/conversation-exports/{job.Id}", ResponseFor(job));
        }, ct);

    [HttpGet("api/conversation-exports/{id:guid}")]
    public async Task<ActionResult<ConversationExportResponse>> Get(Guid id, CancellationToken ct)
    {
        var job = await db.ConversationExportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.WorkspaceId == User.GetWorkspaceId(), ct);
        return job is null ? NotFound() : ResponseFor(job);
    }

    [HttpGet("api/conversation-exports/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var job = await db.ConversationExportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.WorkspaceId == User.GetWorkspaceId(), ct);
        if (job is null) return NotFound();
        if (job.Status == ConversationExportStatus.Expired || job.ExpiresAt <= clock.GetUtcNow()) return StatusCode(410);
        if (job.Status != ConversationExportStatus.Ready || job.ArtifactId is null) return Conflict();
        try
        {
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(files.Open(job.WorkspaceId, ArtifactKind.Exports, job.ArtifactId.Value), "application/x-ndjson", $"conversations-{job.Id:N}.jsonl");
        }
        catch (IOException)
        { logger.LogError("Export artifact unavailable for {JobId} in {WorkspaceId}", job.Id, job.WorkspaceId); return StatusCode(503); }
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/conversation-exports/reconcile")]
    public async Task<IActionResult> Reconcile(Guid workspaceId, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        var now = clock.GetUtcNow();
        var ids = await db.ConversationExportJobs.AsNoTracking().Where(j => j.WorkspaceId == workspaceId && j.ArtifactId != null
            && (j.Status == ConversationExportStatus.Expired || j.Status == ConversationExportStatus.Ready && j.ExpiresAt <= now))
            .OrderBy(j => j.CreatedAt).Take(100).Select(j => j.Id).ToListAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            var artifact = await WriteTransactions.RunAsync<Guid?>(db, async () =>
            {
                var job = await db.ConversationExportJobs.SingleAsync(j => j.Id == id && j.WorkspaceId == workspaceId, ct);
                if (job.Status != ConversationExportStatus.Expired && (job.Status != ConversationExportStatus.Ready || job.ExpiresAt > now)) return null;
                job.Status = ConversationExportStatus.Expired; await db.SaveChangesAsync(ct); return job.ArtifactId;
            }, ct);
            if (artifact is null) continue;
            files.Delete(workspaceId, ArtifactKind.Exports, artifact.Value);
            await db.ConversationExportJobs.Where(j => j.Id == id && j.WorkspaceId == workspaceId && j.Status == ConversationExportStatus.Expired)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ArtifactId, (Guid?)null), ct);
            db.ChangeTracker.Clear(); processed++;
        }
        foreach (var artifact in files.Artifacts(workspaceId, ArtifactKind.Exports))
        {
            if (processed >= 100) break;
            if (artifact.ModifiedAt > now.AddHours(-24) || await db.ConversationExportJobs.AnyAsync(j => j.WorkspaceId == workspaceId && j.ArtifactId == artifact.Id, ct)) continue;
            files.Delete(workspaceId, ArtifactKind.Exports, artifact.Id); processed++;
        }
        processed += files.DeleteOldTemporaryFiles(workspaceId, ArtifactKind.Exports, now.AddHours(-24), 100 - processed);
        return Ok(new { processed });
    }

    private ConversationExportResponse ResponseFor(ConversationExportJob j) => new(j.Id, j.WorkspaceId, j.RequestedByTeammateId,
        j.Status == ConversationExportStatus.Ready && j.ExpiresAt <= clock.GetUtcNow() ? ConversationExportStatus.Expired : j.Status,
        j.CreatedAt, j.SnapshotAt, j.CompletedAt, j.ExpiresAt, j.AttemptCount, j.RowCount, j.ByteLength, j.Sha256, j.FailureCode);
}
