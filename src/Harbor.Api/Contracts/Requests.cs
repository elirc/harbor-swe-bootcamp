using System.ComponentModel.DataAnnotations;
using Harbor.Api.Infrastructure;
using Harbor.Domain;

namespace Harbor.Api.Contracts;

/// <summary>
/// Bootstraps a workspace together with its first admin teammate. The
/// response carries the admin's API key — the only time it is revealed.
/// </summary>
public record CreateWorkspaceRequest(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(200)] string AdminName,
    [Required, EmailAddress, MaxLength(320)] string AdminEmail);

public record CreateInboxRequest(
    [Required, MaxLength(200)] string Name,
    [Range(1, 40_320)] int? FirstResponseSlaMinutes,
    bool AutoAssign = false,
    [EmailAddress, MaxLength(320)] string? EmailAddress = null);

public record UpdateInboxNameRequest([Required, MaxLength(200)] string Name);

public record UpdateInboxAutoAssignRequest([Required] bool? AutoAssign);

public record InboxFilterRequest : PageRequest
{
    public bool IncludeArchived { get; init; }
    public string? Q { get; init; }
    public bool? AutoAssign { get; init; }
}

/// <summary>
/// An inbound email, already parsed by the mail provider. Harbor does not
/// parse MIME: the relay posts the extracted fields.
/// </summary>
public record InboundEmailRequest(
    [Required, EmailAddress, MaxLength(320)] string From,
    [MaxLength(200)] string? FromName,
    [Required, EmailAddress, MaxLength(320)] string To,
    [MaxLength(500)] string? Subject,
    [Required, MaxLength(20_000)] string Body,
    [MaxLength(500)] string? MessageId = null,
    [MaxLength(500)] string? InReplyTo = null,
    IReadOnlyList<string>? References = null);

public record CreateContactRequest(
    [Required, MaxLength(200)] string Name,
    [EmailAddress, MaxLength(320)] string? Email,
    [MaxLength(200)] string? ExternalId,
    IReadOnlyDictionary<string, string?>? Attributes = null);

public record UpdateContactRequest(
    [Required, MaxLength(200)] string Name,
    [EmailAddress, MaxLength(320)] string? Email,
    [MaxLength(200)] string? ExternalId,
    IReadOnlyDictionary<string, string?>? Attributes = null);

/// <summary>Contact membership filters; omitted booleans leave that field unrestricted.</summary>
public record ContactFilterRequest : PageRequest
{
    public string? Q { get; init; }
    public bool? HasEmail { get; init; }
    public bool? HasExternalId { get; init; }
    public bool? HasConversations { get; init; }
}

public record CreateSegmentRequest(
    [Required, MaxLength(200)] string Name,
    [Required] SegmentRuleSet Rules);

public record UpdateSegmentRequest(
    [Required, MaxLength(200)] string Name,
    [Required] SegmentRuleSet Rules);

public record CreateTeammateRequest(
    [Required, MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(320)] string Email,
    TeammateRole Role = TeammateRole.Agent);

public record TeammateFilterRequest : PageRequest
{
    public string? Q { get; init; }

    [EnumDataType(typeof(TeammateAvailability))]
    public TeammateAvailability? Availability { get; init; }

    [EnumDataType(typeof(TeammateRole))]
    public TeammateRole? Role { get; init; }
}

public record CreateTeamRequest(
    [Required, MaxLength(200)] string Name);

/// <summary>Sets a teammate's availability and open-conversation capacity.</summary>
public record UpdateAvailabilityRequest(
    TeammateAvailability Availability,
    [Range(1, 1_000)] int? CapacityLimit);

public record AddTeamMemberRequest(
    [Required] Guid TeammateId);

public record StartConversationRequest(
    [Required] Guid InboxId,
    [Required] Guid ContactId,
    [MaxLength(500)] string? Subject,
    [Required, MaxLength(20_000)] string Body,
    ConversationPriority? Priority = null);

public record AddMessageRequest(
    AuthorType AuthorType,
    [Required] Guid AuthorId,
    MessageKind Kind,
    [Required, MaxLength(20_000)] string Body, [MaxLength(3)] List<Guid>? AttachmentIds = null);

public record ChangeStateRequest(
    ConversationState State,
    DateTimeOffset? SnoozedUntil);

/// <summary>
/// Assigns a conversation. Provide exactly one of TeammateId/TeamId,
/// or neither to unassign.
/// </summary>
public record AssignConversationRequest(Guid? TeammateId, Guid? TeamId);

public record SetPriorityRequest(ConversationPriority Priority);

/// <summary>
/// Creates an SLA policy. Null InboxId/Priority widen the policy to every
/// inbox/priority; at least one target must be set.
/// </summary>
public record CreateSlaPolicyRequest(
    [Required, MaxLength(200)] string Name,
    Guid? InboxId,
    ConversationPriority? Priority,
    [Range(1, 40_320)] int? FirstResponseMinutes,
    [Range(1, 40_320)] int? ResolutionMinutes, Guid? WorkingCalendarId = null);

public record UpdateSlaPolicyRequest(
    [Required, MaxLength(200)] string Name,
    Guid? InboxId,
    ConversationPriority? Priority,
    [Range(1, 40_320)] int? FirstResponseMinutes,
    [Range(1, 40_320)] int? ResolutionMinutes, Guid? WorkingCalendarId = null);

/// <summary>Slug is derived from the name when omitted.</summary>
public record CreateCollectionRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(2_000)] string? Description = null,
    [MaxLength(200)] string? Slug = null);

public record UpdateCollectionRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(2_000)] string? Description = null,
    [MaxLength(200)] string? Slug = null);

/// <summary>Articles are created as drafts; publishing is a separate step.</summary>
public record CreateArticleRequest(
    [Required] Guid CollectionId,
    [Required, MaxLength(300)] string Title,
    [Required, MaxLength(100_000)] string Body,
    [MaxLength(200)] string? Slug = null);

public record UpdateArticleRequest(
    [Required] Guid CollectionId,
    [Required, MaxLength(300)] string Title,
    [Required, MaxLength(100_000)] string Body,
    [MaxLength(200)] string? Slug = null);

/// <summary>Query-string filters for the authenticated article list.</summary>
public record ArticleFilterRequest : PageRequest
{
    public ArticleStatus? Status { get; init; }
    public Guid? CollectionId { get; init; }

    /// <summary>Case-insensitive search across title and body.</summary>
    public string? Q { get; init; }
}

public record CreateTagRequest(
    [Required, MaxLength(100)] string Name);

public record CreateCannedReplyRequest(
    [Required, MaxLength(100)] string Shortcut,
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(20_000)] string Body);

public record UpdateCannedReplyRequest(
    [Required, MaxLength(100)] string Shortcut,
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(20_000)] string Body);

public record CreateWebhookRequest(
    [Required, MaxLength(2_000), Url] string Url,
    [Required, MinLength(1)] IReadOnlyList<WebhookEventType> Events);

public record UpdateWebhookRequest(
    [Required, MaxLength(2_000), Url] string Url,
    [Required, MinLength(1)] IReadOnlyList<WebhookEventType> Events,
    bool IsActive);

/// <summary>Bucket size for the conversation volume report.</summary>
public enum ReportInterval
{
    Hour = 0,
    Day = 1,
    Week = 2,
}

/// <summary>
/// Query-string filters shared by every report endpoint, so the same slice of
/// conversations can be viewed through any of them. From/To bound conversation
/// creation time and are inclusive/exclusive respectively.
/// </summary>
public record ReportFilterRequest
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public Guid? InboxId { get; init; }
    public Guid? AssignedTeammateId { get; init; }
    public Guid? AssignedTeamId { get; init; }
    public ConversationPriority? Priority { get; init; }

    /// <summary>Filter by tag name (exact, case-insensitive).</summary>
    public string? Tag { get; init; }
}

/// <summary>Query-string filters for the conversation list endpoint.</summary>
public record ConversationFilterRequest : PageRequest, IValidatableObject
{
    public bool? HasSubject { get; init; }
    public bool? AwaitingFirstResponse { get; init; }
    public bool? HasTags { get; init; }
    public bool? HasSlaTarget { get; init; }
    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedBefore { get; init; }

    /// <summary>Inclusive creation-time lower bound.</summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>Exclusive creation-time upper bound.</summary>
    public DateTimeOffset? CreatedBefore { get; init; }

    [EnumDataType(typeof(ConversationSort))]
    public ConversationSort Sort { get; init; } = ConversationSort.RecentActivity;

    public ConversationState? State { get; init; }
    public Guid? InboxId { get; init; }
    public Guid? ContactId { get; init; }
    public Guid? AssignedTeammateId { get; init; }
    public Guid? AssignedTeamId { get; init; }

    /// <summary>When true, only conversations with no teammate/team assignment.</summary>
    public bool? Unassigned { get; init; }

    /// <summary>Filter by tag name (exact, case-insensitive).</summary>
    public string? Tag { get; init; }

    /// <summary>Case-insensitive search across subject and message bodies.</summary>
    public string? Q { get; init; }

    /// <summary>
    /// When true, only conversations that missed a first-response or
    /// resolution target.
    /// </summary>
    public bool? SlaBreached { get; init; }

    public ConversationPriority? Priority { get; init; }

    /// <summary>The channel the conversation started on.</summary>
    public MessageChannel? Channel { get; init; }

    /// <summary>Only conversations whose contact currently matches this segment.</summary>
    public Guid? SegmentId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UpdatedFrom is { } updatedFrom && UpdatedBefore is { } updatedBefore && updatedFrom >= updatedBefore)
        {
            yield return new ValidationResult(
                "UpdatedFrom must be earlier than UpdatedBefore.",
                [nameof(UpdatedFrom), nameof(UpdatedBefore)]);
        }
        if (CreatedFrom is { } from && CreatedBefore is { } before && from >= before)
        {
            yield return new ValidationResult(
                "CreatedFrom must be earlier than CreatedBefore.",
                [nameof(CreatedFrom), nameof(CreatedBefore)]);
        }
    }
}

public enum ConversationSort
{
    RecentActivity = 0,
    OldestCreated = 1,
    NewestCreated = 2,
    PriorityFirst = 3,
}

public record MessageFilterRequest : PageRequest
{
    [EnumDataType(typeof(MessageKind))]
    public MessageKind? Kind { get; init; }
}
