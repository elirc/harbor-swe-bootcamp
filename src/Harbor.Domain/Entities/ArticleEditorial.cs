namespace Harbor.Domain.Entities;

public enum ArticleRevisionReason { Edit = 0, Publish = 1, Unpublish = 2 }

public class ArticleRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArticleId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ActorTeammateId { get; set; }
    public ArticleRevisionReason Reason { get; set; }
    public Guid SourceVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string Slug { get; set; }
    public Guid CollectionId { get; set; }
    public ArticleStatus Status { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class ArticleFeedback
{
    public Guid ArticleId { get; set; }
    public Guid TeammateId { get; set; }
    public Guid WorkspaceId { get; set; }
    public bool Helpful { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
