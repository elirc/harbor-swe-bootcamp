using System.ComponentModel.DataAnnotations;
using Harbor.Domain;

namespace Harbor.Api.Contracts;

public record SimulateSlaRequest([Required, NonEmptyGuid] Guid? InboxId,
    [Required, EnumDataType(typeof(ConversationPriority))] ConversationPriority? Priority,
    [Required] DateTimeOffset? CreatedAt);
public record SlaSimulationResponse(string Source, Guid? PolicyId, string? PolicyName,
    int? FirstResponseMinutes, int? ResolutionMinutes, DateTimeOffset? FirstResponseDueAt, DateTimeOffset? ResolutionDueAt,
    Guid? WorkingCalendarId = null);
public record UpcomingSlaRequest : ConversationFilterRequest
{
    [Range(1, 10_080)] public int WithinMinutes { get; init; } = 60;
}
public record UpcomingTargetResponse
{
    public Guid ConversationId { get; init; }
    public string? Subject { get; init; }
    public Guid InboxId { get; init; }
    public SlaBreachKind Kind { get; init; }
    public DateTimeOffset DueAt { get; init; }
}
