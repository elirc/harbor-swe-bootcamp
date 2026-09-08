using System.ComponentModel.DataAnnotations;

namespace Harbor.Api.Contracts;

/// <summary>Required rejects null; this also rejects the otherwise valid all-zero GUID.</summary>
public sealed class NonEmptyGuidAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is Guid id && id != Guid.Empty;
}

public record UpdateSubjectRequest(
    [MaxLength(500)] string? Subject,
    [Required, NonEmptyGuid] Guid? ExpectedVersion);

public record SubjectResponse(Guid Id, string? Subject, Guid Version);
