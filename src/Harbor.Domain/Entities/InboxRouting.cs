namespace Harbor.Domain.Entities;

public record RoutingRule(Guid Id, string Name, Guid TargetTeamId, SegmentRuleSet Conditions,
    ConversationPriority? Priority = null, MessageChannel? Channel = null);

public class InboxRoutingDefinition : IHasVersion
{
    public Guid InboxId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ActiveVersionId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class InboxRoutingVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InboxId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByTeammateId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public required string RulesJson { get; set; }
}
