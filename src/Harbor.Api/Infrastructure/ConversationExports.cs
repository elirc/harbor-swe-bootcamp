using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Infrastructure;

public sealed record ExportClaim(Guid JobId, Guid WorkspaceId, Guid LeaseToken, long Generation, string FilterJson, string? SegmentRulesJson);
public sealed class ExportLimitException(string code) : Exception(code);

public sealed class ConversationExports(DbContextOptions<HarborDbContext> options, PrivateFiles files, TimeProvider clock)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    /// <summary>Test seam for a separate-connection writer after the SQLite snapshot is established.</summary>
    public Func<Task>? AfterSnapshotEstablished { get; set; }

    public async Task<ExportClaim?> ClaimAsync(CancellationToken ct = default)
    {
        await using var db = new HarborDbContext(options);
        return await WriteTransactions.RunAsync<ExportClaim?>(db, async () =>
        {
            var now = clock.GetUtcNow();
            var job = await db.ConversationExportJobs.Where(j => j.Status == ConversationExportStatus.Queued
                || j.Status == ConversationExportStatus.Running && j.LeaseExpiresAt <= now)
                .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).FirstOrDefaultAsync(ct);
            if (job is null) return null;
            if (job.SchemaVersion != 1)
            {
                job.Status = ConversationExportStatus.Failed; job.FailureCode = "UnsupportedFilterVersion";
                job.LeaseToken = null; job.LeaseExpiresAt = null;
                await db.SaveChangesAsync(ct); return null;
            }
            if (job.AttemptCount >= 2)
            {
                job.Status = ConversationExportStatus.Failed; job.FailureCode = "AttemptsExhausted";
                job.LeaseToken = null; job.LeaseExpiresAt = null;
                await db.SaveChangesAsync(ct); return null;
            }
            job.Status = ConversationExportStatus.Running;
            job.LeaseToken = Guid.NewGuid(); job.Generation++; job.AttemptCount++;
            job.LeaseExpiresAt = now.AddMinutes(5); job.FailureCode = null;
            await db.SaveChangesAsync(ct);
            return new(job.Id, job.WorkspaceId, job.LeaseToken.Value, job.Generation, job.FilterJson, job.CapturedSegmentRulesJson);
        }, ct);
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        var claim = await ClaimAsync(ct);
        if (claim is null) return false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var token = budget.Token;
            await using var read = new HarborDbContext(options);
            await read.Database.OpenConnectionAsync(token);
            var connection = (SqliteConnection)read.Database.GetDbConnection();
            await using var snapshot = connection.BeginTransaction(deferred: true);
            await using var enlistment = await read.Database.UseTransactionAsync(snapshot, token);
            await read.Workspaces.AnyAsync(w => w.Id == claim.WorkspaceId, token); // Establish the snapshot before recording its time.
            var snapshotAt = clock.GetUtcNow();
            var filter = JsonSerializer.Deserialize<ConversationFilterRequest>(claim.FilterJson, Json)!;
            var query = (await new ConversationQueries(read).BuildAsync(claim.WorkspaceId, filter with { SegmentId = null }, snapshotAt, token))!;
            if (claim.SegmentRulesJson is { } rules)
            {
                var ids = SegmentQueries.ContactIdsQuery(read, claim.WorkspaceId, new Segment { WorkspaceId = claim.WorkspaceId, Name = "Captured export filter", RulesJson = rules });
                query = query.Where(c => ids.Contains(c.ContactId));
            }
            var count = await query.Take(10_001).CountAsync(token);
            if (count > 10_000) throw new ExportLimitException("RowLimitExceeded");
            if (AfterSnapshotEstablished is { } hook) await hook();
            var artifact = await files.CreateAsync(claim.WorkspaceId, ArtifactKind.Exports, 50L * 1024 * 1024, async output =>
            {
                await using var writer = new StreamWriter(output, new UTF8Encoding(false), 81920, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "manifest", version = 1, snapshotAt, rowCount = count,
                    filters = JsonSerializer.Deserialize<JsonElement>(claim.FilterJson),
                    capturedSegmentRules = claim.SegmentRulesJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(claim.SegmentRulesJson) }, Json).AsMemory(), token);
                for (var offset = 0; offset < count; offset += 250)
                {
                    token.ThrowIfCancellationRequested();
                    var page = await query.Include(c => c.Tags).ThenInclude(t => t.Tag).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                        .Skip(offset).Take(250).ToListAsync(token);
                    foreach (var conversation in page) await writer.WriteLineAsync(JsonSerializer.Serialize(conversation.ToSummaryResponse(), Json).AsMemory(), token);
                }
                await writer.FlushAsync(token);
            }, token);
            await snapshot.CommitAsync(token);
            await enlistment!.DisposeAsync();
            await read.Database.CloseConnectionAsync();
            // Complete outside the read transaction. A stale generation cannot publish its file.
            if (!await CompleteAsync(claim, artifact, snapshotAt, count, token)) files.Delete(claim.WorkspaceId, ArtifactKind.Exports, artifact.StorageId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } // Lease recovery owns interrupted processing.
        catch (OperationCanceledException) { await FailAsync(claim, "ProcessingBudgetExceeded", CancellationToken.None); }
        catch (ExportLimitException ex) { await FailAsync(claim, ex.Message, ct); }
        catch (ArtifactLimitException) { await FailAsync(claim, "ByteLimitExceeded", ct); }
        catch (Exception ex) when (ex is IOException or JsonException or DomainException)
        { await FailAsync(claim, ex is IOException ? "StorageUnavailable" : "InvalidCapturedFilter", ct); }
        return true;
    }

    public async Task<bool> CompleteAsync(ExportClaim claim, StoredArtifact artifact, DateTimeOffset snapshotAt, int count, CancellationToken ct = default)
    {
        await using var db = new HarborDbContext(options);
        return await WriteTransactions.RunAsync(db, async () =>
        {
            var now = clock.GetUtcNow();
            var job = await db.ConversationExportJobs.SingleOrDefaultAsync(j => j.Id == claim.JobId && j.WorkspaceId == claim.WorkspaceId, ct);
            if (job?.Status == ConversationExportStatus.Ready && job.ArtifactId == artifact.StorageId) return true;
            if (job is null || !Owns(job, claim, now)) return false;
            job.Status = ConversationExportStatus.Ready; job.ArtifactId = artifact.StorageId;
            job.RowCount = count; job.ByteLength = artifact.Length; job.Sha256 = artifact.Sha256;
            job.SnapshotAt = snapshotAt; job.CompletedAt = now; job.ExpiresAt = now.AddHours(24);
            job.LeaseToken = null; job.LeaseExpiresAt = null;
            await db.SaveChangesAsync(ct); return true;
        }, ct);
    }

    private async Task FailAsync(ExportClaim claim, string code, CancellationToken ct)
    {
        await using var db = new HarborDbContext(options);
        await WriteTransactions.RunAsync(db, async () =>
        {
            var job = await db.ConversationExportJobs.SingleOrDefaultAsync(j => j.Id == claim.JobId && j.WorkspaceId == claim.WorkspaceId, ct);
            if (job is null || !Owns(job, claim, clock.GetUtcNow())) return false;
            job.Status = ConversationExportStatus.Failed; job.FailureCode = code;
            job.LeaseToken = null; job.LeaseExpiresAt = null; await db.SaveChangesAsync(ct); return true;
        }, ct);
    }
    private static bool Owns(ConversationExportJob job, ExportClaim claim, DateTimeOffset now) =>
        job.Status == ConversationExportStatus.Running && job.LeaseToken == claim.LeaseToken && job.Generation == claim.Generation && job.LeaseExpiresAt > now;
}
