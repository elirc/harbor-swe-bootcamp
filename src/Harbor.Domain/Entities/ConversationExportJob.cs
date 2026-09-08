namespace Harbor.Domain.Entities;

public enum ConversationExportStatus { Queued = 0, Running = 1, Ready = 2, Failed = 3, Expired = 4 }
public class ConversationExportJob : IHasVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid RequestedByTeammateId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string FilterJson { get; set; }
    public string? CapturedSegmentRulesJson { get; set; }
    public ConversationExportStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SnapshotAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public long Generation { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public Guid? ArtifactId { get; set; }
    public int? RowCount { get; set; }
    public long? ByteLength { get; set; }
    public string? Sha256 { get; set; }
    public string? FailureCode { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
