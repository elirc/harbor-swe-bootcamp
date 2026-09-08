namespace Harbor.Domain.Entities;

public enum ConfigurationAuditAction { Created = 0, Updated = 1, Deleted = 2 }
public enum ConfigurationEntityKind { TeammateAvailability = 0, TeamMembership = 1, CannedReply = 2 }
public class ConfigurationAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ActorTeammateId { get; set; }
    public Guid EntityId { get; set; }
    public Guid? RelatedTeammateId { get; set; }
    public ConfigurationAuditAction Action { get; set; }
    public ConfigurationEntityKind EntityKind { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string ChangedFieldsJson { get; set; }
    public string? AllowedChangesJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
