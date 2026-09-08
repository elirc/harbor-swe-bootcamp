namespace Harbor.Domain.Entities;

public class ConversationChecklistItem : IHasVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public required string Title { get; set; }
    public bool Required { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedByTeammateId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
