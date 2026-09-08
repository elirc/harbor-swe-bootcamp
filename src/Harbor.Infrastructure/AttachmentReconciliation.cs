using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public sealed class AttachmentReconciliation(HarborDbContext db, PrivateFiles files)
{
    public async Task<int> ReconcileAsync(Guid workspaceId, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.MessageAttachments.AsNoTracking().Where(a => a.WorkspaceId == workspaceId
            && (a.State == AttachmentState.Deleting || a.State == AttachmentState.Uploaded && a.ExpiresAt <= now))
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Take(100).Select(a => a.Id).ToListAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            var storage = await WriteTransactions.RunAsync<Guid?>(db, async () =>
            {
                var row = await db.MessageAttachments.SingleOrDefaultAsync(a => a.Id == id && a.WorkspaceId == workspaceId, ct);
                if (row is null || row.State == AttachmentState.Attached || row.State == AttachmentState.Uploaded && row.ExpiresAt > now) return null;
                row.State = AttachmentState.Deleting;
                await db.SaveChangesAsync(ct);
                return row.StorageId;
            }, ct);
            if (storage is null) continue;
            files.Delete(workspaceId, ArtifactKind.Attachments, storage.Value);
            await db.MessageAttachments.Where(a => a.Id == id && a.WorkspaceId == workspaceId && a.State == AttachmentState.Deleting).ExecuteDeleteAsync(ct);
            db.ChangeTracker.Clear();
            processed++;
        }
        // Renamed blobs without metadata can follow an interrupted upload. Never remove a referenced blob.
        foreach (var artifact in files.Artifacts(workspaceId, ArtifactKind.Attachments))
        {
            if (processed >= 100) break;
            if (artifact.ModifiedAt > now.AddHours(-24)) continue;
            if (await db.MessageAttachments.AnyAsync(a => a.WorkspaceId == workspaceId && a.StorageId == artifact.Id, ct)) continue;
            files.Delete(workspaceId, ArtifactKind.Attachments, artifact.Id);
            processed++;
        }
        return processed + files.DeleteOldTemporaryFiles(workspaceId, ArtifactKind.Attachments, now.AddHours(-24), 100 - processed);
    }
}
