namespace Harbor.Domain.Entities;

/// <summary>Id identifies an immutable UTC calendar version; there is no update endpoint.</summary>
public class WorkingCalendar
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByTeammateId { get; set; }
    public required string Name { get; set; }
    public required string DefinitionJson { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public class SlaRecalculationPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByTeammateId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string Fingerprint { get; set; }
    public required string ItemsJson { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
}
