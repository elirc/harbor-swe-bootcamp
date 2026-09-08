using System.ComponentModel.DataAnnotations;
using Harbor.Domain;
using Harbor.Infrastructure;

namespace Harbor.Api.Contracts;

public record PreviewSegmentRequest([Required] SegmentRuleSet Rules, [Range(1, 25)] int SampleLimit = 10) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SegmentRuleShape.Error(Rules) is { } error) yield return new ValidationResult(error, [nameof(Rules)]);
    }
}
public record SegmentPreviewResponse(int TotalMatches, IReadOnlyList<ContactResponse> Sample);
public record CloneSegmentRequest([Required, MaxLength(200)] string Name);
public record SegmentConditionResult(int ConditionIndex, string Field, SegmentOperator Operator, string? Value, bool Matches);
public record SegmentExplanationResponse(Guid SegmentId, Guid ContactId, SegmentMatch Match, bool Matches,
    IReadOnlyList<SegmentConditionResult> Conditions);
