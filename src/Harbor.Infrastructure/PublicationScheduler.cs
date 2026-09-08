using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

/// <summary>Each due row is rechecked under a short writer transaction. No external effects.</summary>
public sealed class PublicationScheduler(DbContextOptions<HarborDbContext> options)
{
    public async Task<int> ProcessDueAsync(DateTimeOffset now, int limit = 20, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 20);
        await using var scan = new HarborDbContext(options);
        var ids = await scan.ArticlePublicationSchedules.AsNoTracking()
            .Where(s => s.Status == PublicationScheduleStatus.Pending && s.PublishAt <= now)
            .OrderBy(s => s.PublishAt).ThenBy(s => s.Id).Take(limit).Select(s => s.Id).ToListAsync(ct);
        var processed = 0;
        foreach (var id in ids)
        {
            await using var db = new HarborDbContext(options);
            processed += await WriteTransactions.RunAsync(db, async () =>
            {
                var schedule = await db.ArticlePublicationSchedules.SingleOrDefaultAsync(s => s.Id == id, ct);
                if (schedule is null || schedule.Status != PublicationScheduleStatus.Pending || schedule.PublishAt > now) return 0;
                var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == schedule.ArticleId && a.WorkspaceId == schedule.WorkspaceId, ct);
                var authorStillAdmin = await db.Teammates.AnyAsync(t => t.Id == schedule.ScheduledByTeammateId
                    && t.WorkspaceId == schedule.WorkspaceId && t.Role == TeammateRole.Admin, ct);
                string? reason = !authorStillAdmin ? "AuthorNoLongerAdmin" : article is null ? "ArticleUnavailable"
                    : article.Version != schedule.ExpectedArticleVersion ? "ArticleChanged"
                    : article.Status != ArticleStatus.Draft ? "ArticleNotDraft" : null;
                if (reason is null)
                {
                    try { await ArticlePublications.PublishAsync(db, article!, schedule.ScheduledByTeammateId, now, scheduled: true); }
                    catch (PublicationConflictException) { reason = "PublicSlugConflict"; }
                    catch (DomainException) { reason = "CollectionUnavailable"; }
                }
                schedule.Status = reason is null ? PublicationScheduleStatus.Completed : PublicationScheduleStatus.NeedsReview;
                schedule.Reason = reason;
                schedule.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                return 1;
            }, ct);
        }
        return processed;
    }
}
