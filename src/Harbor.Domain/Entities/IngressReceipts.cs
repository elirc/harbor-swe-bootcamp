namespace Harbor.Domain.Entities;

public class CreationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid TeammateId { get; set; }
    public string Operation { get; set; } = "Conversation.Start";
    public required string Key { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string RequestHash { get; set; }
    public int ResponseStatus { get; set; } = 201;
    public string ContentType { get; set; } = "application/json; charset=utf-8";
    public required string ResponseJson { get; set; }
    public required string Location { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset RetainUntil { get; set; }
}

public class InboundEmailReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid InboxId { get; set; }
    public required string MessageId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string RequestHash { get; set; }
    public Guid ConversationId { get; set; }
    public Guid MessageRecordId { get; set; }
    public Guid ContactId { get; set; }
    public bool CreatedConversation { get; set; }
    public bool CreatedContact { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
