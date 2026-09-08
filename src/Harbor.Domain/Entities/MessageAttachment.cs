namespace Harbor.Domain.Entities;

public enum AttachmentState { Uploaded = 0, Attached = 1, Deleting = 2 }
public class MessageAttachment : IHasVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UploadedByTeammateId { get; set; }
    public Guid? MessageId { get; set; }
    public required string FileName { get; set; }
    public long ByteLength { get; set; }
    public required string Sha256 { get; set; }
    public Guid StorageId { get; set; }
    public AttachmentState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
