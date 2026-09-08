namespace Harbor.Domain.Entities;

public class ContactMergePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByTeammateId { get; set; }
    public Guid? CommittedByTeammateId { get; set; }
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string Fingerprint { get; set; }
    public required string PreviewJson { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
}

public enum ContactImportStatus { Ready = 0, Invalid = 1, Committed = 2 }

public class ContactImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByTeammateId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public ContactImportStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public ICollection<ContactImportRow> Rows { get; set; } = new List<ContactImportRow>();
}

public class ContactImportRow
{
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string? NormalizedJson { get; set; }
    public string ErrorsJson { get; set; } = "[]";
    public Guid? ContactId { get; set; }
    public ContactImportBatch? Batch { get; set; }
}
