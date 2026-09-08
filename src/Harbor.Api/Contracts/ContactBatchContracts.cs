using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Api.Contracts;

public record PreviewContactMergeRequest([Required, NonEmptyGuid] Guid? SourceId, [Required, NonEmptyGuid] Guid? TargetId);
public record ContactMergePlanResponse(Guid Id, DateTimeOffset ExpiresAt, MergePreview Preview);
public record ContactMergeResult(Guid PlanId, Guid SourceId, Guid TargetId, int MovedConversations, int RepointedMessages, DateTimeOffset CommittedAt);
public record StageContactImportRequest([Required, MinLength(1), MaxLength(200)] List<JsonElement>? Rows);
public record ContactImportRowResponse(int RowNumber, ValidatedImportContact? Contact, IReadOnlyList<ImportFieldError> Errors, Guid? ContactId);
public record ContactImportResponse(Guid Id, ContactImportStatus Status, DateTimeOffset ExpiresAt, DateTimeOffset? CommittedAt,
    IReadOnlyList<ContactImportRowResponse> Rows);
