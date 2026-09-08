using System.ComponentModel.DataAnnotations;
using Harbor.Domain;

namespace Harbor.Api.Contracts;

/// <summary>A version-one query recipe. Paging belongs to each execution, not the recipe.</summary>
public record SavedQueueFilter : IValidatableObject
{
    [EnumDataType(typeof(ConversationState))] public ConversationState? State { get; init; }
    [EnumDataType(typeof(ConversationPriority))] public ConversationPriority? Priority { get; init; }
    [EnumDataType(typeof(MessageChannel))] public MessageChannel? Channel { get; init; }
    [EnumDataType(typeof(ConversationSort))] public ConversationSort Sort { get; init; }
    public Guid? InboxId { get; init; }
    public Guid? ContactId { get; init; }
    public Guid? AssignedTeammateId { get; init; }
    public Guid? AssignedTeamId { get; init; }
    public Guid? SegmentId { get; init; }
    public bool? Unassigned { get; init; }
    public bool? SlaBreached { get; init; }
    public bool? HasSubject { get; init; }
    public bool? AwaitingFirstResponse { get; init; }
    public bool? HasTags { get; init; }
    public bool? HasSlaTarget { get; init; }
    public string? Q { get; init; }
    public string? Tag { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedBefore { get; init; }
    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedBefore { get; init; }

    public ConversationFilterRequest ToRuntime(int? page = null, int? pageSize = null) => new()
    {
        State = State, Priority = Priority, Channel = Channel, Sort = Sort,
        InboxId = InboxId, ContactId = ContactId, AssignedTeammateId = AssignedTeammateId,
        AssignedTeamId = AssignedTeamId, SegmentId = SegmentId, Unassigned = Unassigned,
        SlaBreached = SlaBreached, HasSubject = HasSubject, AwaitingFirstResponse = AwaitingFirstResponse,
        HasTags = HasTags, HasSlaTarget = HasSlaTarget, Q = Q, Tag = Tag,
        CreatedFrom = CreatedFrom, CreatedBefore = CreatedBefore,
        UpdatedFrom = UpdatedFrom, UpdatedBefore = UpdatedBefore, Page = page, PageSize = pageSize,
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ToRuntime().Validate(validationContext);
}

public record SaveQueueRequest([Required, MaxLength(100)] string Name, [Required] SavedQueueFilter Filters);
public record SavedQueueResponse(Guid Id, string Name, SavedQueueFilter Filters, int SchemaVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record SaveDraftRequest([Required, MaxLength(20_000)] string Body, Guid? ExpectedVersion);
public record DraftResponse(string Body, Guid Version, DateTimeOffset UpdatedAt);
