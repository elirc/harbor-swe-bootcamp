namespace Harbor.Domain.Entities;

public enum WorkspaceEventKind { ConversationCreated = 0, MessageCreated = 1, StateChanged = 2, AssignmentChanged = 3 }
public class WorkspaceEvent
{
    public long Sequence { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public WorkspaceEventKind Kind { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid EntityId { get; set; }
    public Guid ConversationId { get; set; }
    public required string MetadataJson { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
public class WorkspaceEventWatermark
{
    public Guid WorkspaceId { get; set; }
    public long PurgedThrough { get; set; }
}
