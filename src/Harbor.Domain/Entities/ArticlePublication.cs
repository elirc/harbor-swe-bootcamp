namespace Harbor.Domain.Entities;

public class ArticlePublicationSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArticleId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid? PublishedByTeammateId { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? FirstPublishedAt { get; set; }
    public DateTimeOffset ArticleCreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ActiveArticlePublication
{
    public Guid ArticleId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid SnapshotId { get; set; }
    public required string Slug { get; set; }
    public ArticlePublicationSnapshot? Snapshot { get; set; }
}

public enum PublicationScheduleStatus { Pending = 0, Completed = 1, Cancelled = 2, NeedsReview = 3 }

public class ArticlePublicationSchedule : IHasVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArticleId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ScheduledByTeammateId { get; set; }
    public Guid ExpectedArticleVersion { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset PublishAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public PublicationScheduleStatus Status { get; set; }
    public string? Reason { get; set; }
}
