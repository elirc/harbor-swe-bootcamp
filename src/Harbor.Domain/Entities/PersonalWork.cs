namespace Harbor.Domain.Entities;

public class SavedQueue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid OwnerTeammateId { get; set; }
    public string Name { get; set; } = "";
    public string FilterJson { get; set; } = "{}";
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ConversationBookmark
{
    public Guid WorkspaceId { get; set; }
    public Guid TeammateId { get; set; }
    public Guid ConversationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Conversation? Conversation { get; set; }
}

public class ReplyDraft : IHasVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid OwnerTeammateId { get; set; }
    public string Body { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
