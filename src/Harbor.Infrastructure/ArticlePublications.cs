using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public sealed class PublicationConflictException(string message) : Exception(message);

public static class ArticlePublications
{
    public static IQueryable<ArticlePublicationSnapshot> Public(HarborDbContext db, Guid workspaceId) =>
        db.ActiveArticlePublications.AsNoTracking().Where(p => p.WorkspaceId == workspaceId
            && p.Snapshot!.WorkspaceId == workspaceId && p.Snapshot.ArticleId == p.ArticleId)
            .Select(p => p.Snapshot!);

    public static Article ForRanking(ArticlePublicationSnapshot snapshot) => new()
    {
        Id = snapshot.ArticleId, WorkspaceId = snapshot.WorkspaceId, CollectionId = snapshot.CollectionId,
        Title = snapshot.Title, Slug = snapshot.Slug, Body = snapshot.Body, Status = ArticleStatus.Published,
        CreatedAt = snapshot.ArticleCreatedAt, UpdatedAt = snapshot.UpdatedAt, PublishedAt = snapshot.FirstPublishedAt,
    };

    public static async Task InvalidateScheduleAsync(HarborDbContext db, Guid articleId, DateTimeOffset now, bool manualPublication)
    {
        var schedule = await db.ArticlePublicationSchedules.SingleOrDefaultAsync(s => s.ArticleId == articleId && s.Status == PublicationScheduleStatus.Pending);
        if (schedule is null) return;
        schedule.Status = manualPublication ? PublicationScheduleStatus.Cancelled : PublicationScheduleStatus.NeedsReview;
        schedule.Reason = manualPublication ? "ManualPublicationChange" : "ArticleEdited";
        schedule.UpdatedAt = now;
    }

    /// <summary>Stage approved content and its route. Caller owns the transaction and save.</summary>
    public static async Task<bool> PublishAsync(HarborDbContext db, Article article, Guid actorId, DateTimeOffset now, bool scheduled = false)
    {
        if (!await db.ArticleCollections.AnyAsync(c => c.Id == article.CollectionId && c.WorkspaceId == article.WorkspaceId))
            throw new DomainException("The editorial collection is unavailable.");
        if (await db.ActiveArticlePublications.AnyAsync(p => p.WorkspaceId == article.WorkspaceId && p.Slug == article.Slug && p.ArticleId != article.Id))
            throw new PublicationConflictException("The slug belongs to another active public article.");
        var active = await db.ActiveArticlePublications.SingleOrDefaultAsync(p => p.ArticleId == article.Id && p.WorkspaceId == article.WorkspaceId);
        article.ActivePublication = active;
        if (!scheduled) await InvalidateScheduleAsync(db, article.Id, now, manualPublication: true);
        if (active is not null && !article.HasUnpublishedChanges && article.Status == ArticleStatus.Published) return false;
        ArticleHistory.Snapshot(db, article, ArticleRevisionReason.Publish, actorId, now);
        article.Publish(now);
        // Publishing a different approved copy is a versioned change even under a fixed clock.
        db.Entry(article).Property(a => a.UpdatedAt).IsModified = true;
        var snapshot = Snapshot(article, actorId, now);
        db.ArticlePublicationSnapshots.Add(snapshot);
        if (active is null)
        {
            active = new ActiveArticlePublication { ArticleId = article.Id, WorkspaceId = article.WorkspaceId,
                CollectionId = article.CollectionId, Slug = article.Slug, SnapshotId = snapshot.Id, Snapshot = snapshot };
            db.ActiveArticlePublications.Add(active);
        }
        else
        {
            active.CollectionId = article.CollectionId; active.Slug = article.Slug;
            active.SnapshotId = snapshot.Id; active.Snapshot = snapshot;
        }
        article.ActivePublication = active;
        return true;
    }

    public static async Task UnpublishAsync(HarborDbContext db, Article article, Guid actorId, DateTimeOffset now)
    {
        await InvalidateScheduleAsync(db, article.Id, now, manualPublication: true);
        var active = await db.ActiveArticlePublications.SingleOrDefaultAsync(p => p.ArticleId == article.Id);
        if (active is not null) db.ActiveArticlePublications.Remove(active);
        article.ActivePublication = null;
        if (article.Status == ArticleStatus.Draft) return;
        ArticleHistory.Snapshot(db, article, ArticleRevisionReason.Unpublish, actorId, now);
        article.Unpublish(now);
    }

    public static void StageSeedPublication(HarborDbContext db, Article article)
    {
        if (article.Status != ArticleStatus.Published) return;
        var snapshot = Snapshot(article, null, article.PublishedAt ?? article.UpdatedAt);
        var active = new ActiveArticlePublication { ArticleId = article.Id, WorkspaceId = article.WorkspaceId,
            CollectionId = article.CollectionId, Slug = article.Slug, SnapshotId = snapshot.Id, Snapshot = snapshot };
        db.ArticlePublicationSnapshots.Add(snapshot);
        db.ActiveArticlePublications.Add(active);
        article.ActivePublication = active;
    }

    private static ArticlePublicationSnapshot Snapshot(Article article, Guid? actor, DateTimeOffset now) => new()
    {
        ArticleId = article.Id, WorkspaceId = article.WorkspaceId, CollectionId = article.CollectionId,
        PublishedByTeammateId = actor, Title = article.Title, Slug = article.Slug, Body = article.Body,
        PublishedAt = now, FirstPublishedAt = article.PublishedAt, ArticleCreatedAt = article.CreatedAt, UpdatedAt = article.UpdatedAt,
    };
}
