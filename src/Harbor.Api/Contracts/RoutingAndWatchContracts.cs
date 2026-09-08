using System.ComponentModel.DataAnnotations;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Api.Contracts;

public record SetRoutingRulesRequest(Guid? ExpectedVersion, [Required] IReadOnlyList<RoutingRule>? Rules);
public record RoutingRulesResponse(Guid InboxId, Guid? Version, Guid? ActiveVersionId, IReadOnlyList<RoutingRule> Rules);
public record PreviewRoutingRequest([Required, NonEmptyGuid] Guid? ContactId,
    [EnumDataType(typeof(ConversationPriority))] ConversationPriority Priority = ConversationPriority.Normal,
    [EnumDataType(typeof(MessageChannel))] MessageChannel Channel = MessageChannel.Chat);
public record ReplyNotificationResponse(Guid Id, Guid ConversationId, Guid MessageId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
