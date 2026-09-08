using System.ComponentModel.DataAnnotations;

namespace Harbor.Api.Contracts;

public record CreateApiKeyRequest([Required, MaxLength(100)] string Name, [Required, Range(15, 43_200)] int? LifetimeMinutes);
public record ApiKeyMetadata(Guid Id, Guid TeammateId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt);
public record CreatedApiKeyResponse(ApiKeyMetadata Credential, string ApiKey);
