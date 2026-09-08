using System.ComponentModel.DataAnnotations;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Api.Contracts;

public record CloneArticleRequest([Required, NonEmptyGuid] Guid? CollectionId,
    [Required, MaxLength(300)] string Title, [MaxLength(200)] string? Slug = null);
public record ArticleRevisionMetadata(Guid Id, Guid ArticleId, Guid ActorTeammateId,
    ArticleRevisionReason Reason, Guid SourceVersion, DateTimeOffset RecordedAt);
public record ArticleRevisionResponse(Guid Id, Guid ArticleId, Guid ActorTeammateId, ArticleRevisionReason Reason,
    Guid SourceVersion, DateTimeOffset RecordedAt, string Title, string Body, string Slug, Guid CollectionId,
    ArticleStatus Status, DateTimeOffset? PublishedAt);
public record SetArticleFeedbackRequest([Required] bool? Helpful);
public record ArticleFeedbackResponse(bool Helpful, DateTimeOffset UpdatedAt);
public record ArticleFeedbackSummary(int Total, int Helpful, int Unhelpful);
