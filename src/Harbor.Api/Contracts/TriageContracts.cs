using System.ComponentModel.DataAnnotations;
using Harbor.Domain;

namespace Harbor.Api.Contracts;

public record BulkPriorityRequest(
    [Required, MinLength(1), MaxLength(25)] IReadOnlyList<Guid> ConversationIds,
    [Required, EnumDataType(typeof(ConversationPriority))] ConversationPriority? Priority) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ConversationIds is not null && (ConversationIds.Any(id => id == Guid.Empty)
            || ConversationIds.Distinct().Count() != ConversationIds.Count))
            yield return new ValidationResult("Conversation IDs must be distinct and nonempty.", [nameof(ConversationIds)]);
    }
}

public record ReplaceTagsRequest([Required, MaxLength(20)] IReadOnlyList<Guid> TagIds) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TagIds is not null && (TagIds.Any(id => id == Guid.Empty) || TagIds.Distinct().Count() != TagIds.Count))
            yield return new ValidationResult("Tag IDs must be distinct and nonempty.", [nameof(TagIds)]);
    }
}

public record WakeDueResponse(IReadOnlyList<Guid> ConversationIds, DateTimeOffset EvaluatedAt);
public record EligibilityResponse(Guid Id, string Name, TeammateAvailability Availability,
    int AssignedCount, int? CapacityLimit, bool Eligible, IReadOnlyList<string> Reasons);
public enum TimelineSource { Message = 0, Assignment = 1, SlaBreach = 2 }
public record TimelineResponse(Guid Id, TimelineSource Kind, DateTimeOffset OccurredAt,
    MessageResponse? Message, AssignmentEventResponse? Assignment, SlaBreachEventResponse? SlaBreach);
