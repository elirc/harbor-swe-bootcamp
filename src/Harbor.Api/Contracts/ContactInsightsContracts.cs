using System.ComponentModel.DataAnnotations;

namespace Harbor.Api.Contracts;

public record TransferConversationRequest([Required, NonEmptyGuid] Guid? DestinationInboxId);
public record TransferEventResponse(Guid Id, Guid ConversationId, Guid ActorTeammateId,
    Guid FromInboxId, Guid ToInboxId, DateTimeOffset CreatedAt);
public record PatchAttributesRequest([Required] IReadOnlyDictionary<string, string?> Changes,
    [Required, NonEmptyGuid] Guid? ExpectedVersion);
public record ContactAttributesResponse(IReadOnlyDictionary<string, string?> Attributes, Guid Version);
public record DuplicateEmailResponse(string NormalizedEmail, int ContactCount);
public record ContactSupportResponse(int TotalConversations, int Open, int Snoozed, int Closed,
    int Replies, int Notes, DateTimeOffset? FirstConversationAt, DateTimeOffset? LatestMessageAt, Guid? LatestConversationId);
