namespace Harbor.Domain.Entities;

public class TeammateApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid TeammateId { get; set; }
    public required string Name { get; set; }
    public required string Hash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Teammate? Teammate { get; set; }
}
