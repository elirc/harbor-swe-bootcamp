using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

public static class ArticleHistory
{
    /// <summary>Call after validation and before mutation; the caller commits history and article together.</summary>
    public static void Snapshot(HarborDbContext db, Article article, ArticleRevisionReason reason, Guid actorId, DateTimeOffset now) =>
        db.ArticleRevisions.Add(new ArticleRevision
        {
            ArticleId = article.Id, WorkspaceId = article.WorkspaceId, ActorTeammateId = actorId,
            Reason = reason, SourceVersion = article.Version, RecordedAt = now,
            Title = article.Title, Body = article.Body, Slug = article.Slug, CollectionId = article.CollectionId,
            Status = article.Status, PublishedAt = article.PublishedAt,
        });
}
