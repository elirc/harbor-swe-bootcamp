namespace Harbor.Domain.Entities;

public class ConversationTransferEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid ActorTeammateId { get; set; }
    public Guid FromInboxId { get; set; }
    public Guid ToInboxId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
