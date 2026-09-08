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
public sealed class AttachmentsController(HarborDbContext db, PrivateFiles files, TimeProvider clock, ILogger<AttachmentsController> logger) : ControllerBase
{
    [HttpPost("api/conversations/{id:guid}/attachments")]
    [RequestSizeLimit(5 * 1024 * 1024 + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024 + 64 * 1024)]
    public async Task<ActionResult<AttachmentResponse>> Upload(Guid id, IFormFile file, CancellationToken ct)
    {
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct);
        if (conversation is null) return NotFound();
        if (conversation.Channel != MessageChannel.Chat) return UnprocessableEntity();
        if (Request.Form.Files.Count != 1) return BadRequest();
        if (file.Length > 5 * 1024 * 1024) return StatusCode(413);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        StoredArtifact artifact;
        try { await using var source = file.OpenReadStream(); artifact = await files.StoreAsync(conversation.WorkspaceId, ArtifactKind.Attachments, source, 5 * 1024 * 1024, budget.Token); }
        catch (ArtifactLimitException) { return StatusCode(413); }
        var now = clock.GetUtcNow();
        var row = new MessageAttachment { WorkspaceId = conversation.WorkspaceId, ConversationId = id, UploadedByTeammateId = User.GetTeammateId(),
            FileName = PrivateFiles.SafeDisplayName(file.FileName), ByteLength = artifact.Length, Sha256 = artifact.Sha256, StorageId = artifact.StorageId,
            CreatedAt = now, ExpiresAt = now.AddHours(24) };
        db.MessageAttachments.Add(row);
        // An interrupted commit can have an uncertain outcome. Keep the private blob;
        // reconciliation removes it after retention if no metadata references it.
        await db.SaveChangesAsync(budget.Token);
        return Created($"/api/attachments/{row.Id}/content", row.ToResponse());
    }

    [HttpGet("api/attachments/{id:guid}/content")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var row = await db.MessageAttachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId(), ct);
        if (row is null || row.State == AttachmentState.Deleting) return NotFound();
        if (row.State == AttachmentState.Uploaded)
        {
            if (row.UploadedByTeammateId != User.GetTeammateId()) return NotFound();
            if (row.ExpiresAt <= clock.GetUtcNow()) return StatusCode(410);
        }
        else if (!await db.Messages.AnyAsync(m => m.Id == row.MessageId && m.ConversationId == row.ConversationId
            && m.Conversation!.WorkspaceId == row.WorkspaceId, ct)) return NotFound();
        try
        {
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(files.Open(row.WorkspaceId, ArtifactKind.Attachments, row.StorageId), "application/octet-stream", row.FileName);
        }
        catch (IOException)
        {
            logger.LogError("Attachment blob unavailable for {AttachmentId} in {WorkspaceId}", row.Id, row.WorkspaceId);
            return StatusCode(503, new ProblemDetails { Title = "Attachment storage is temporarily unavailable.", Status = 503 });
        }
    }

    [HttpDelete("api/attachments/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Guid? storage = null;
        var status = await WriteTransactions.RunAsync(db, async () =>
        {
            var row = await db.MessageAttachments.SingleOrDefaultAsync(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId() && a.UploadedByTeammateId == User.GetTeammateId(), ct);
            if (row is null) return 404;
            if (row.State == AttachmentState.Attached) return 409;
            row.State = AttachmentState.Deleting;
            storage = row.StorageId;
            await db.SaveChangesAsync(ct);
            return 204;
        }, ct);
        if (status != 204) return StatusCode(status);
        files.Delete(User.GetWorkspaceId(), ArtifactKind.Attachments, storage!.Value);
        await db.MessageAttachments.Where(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId() && a.State == AttachmentState.Deleting).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/attachments/reconcile"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reconcile(Guid workspaceId, [FromServices] AttachmentReconciliation reconciliation, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        return Ok(new { processed = await reconciliation.ReconcileAsync(workspaceId, clock.GetUtcNow(), ct) });
    }
}
