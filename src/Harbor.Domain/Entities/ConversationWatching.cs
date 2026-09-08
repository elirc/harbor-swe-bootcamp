namespace Harbor.Domain.Entities;

public class ConversationWatcher
{
    public Guid ConversationId { get; set; }
    public Guid TeammateId { get; set; }
    public Guid WorkspaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Teammate? Teammate { get; set; }
}

public class ReplyNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid TeammateId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
