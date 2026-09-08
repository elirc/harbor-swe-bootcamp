using Harbor.Domain;

namespace Harbor.Infrastructure;

/// <summary>Checks structure before handing rules to the existing semantic compiler.</summary>
public static class SegmentRuleShape
{
    public static string? Error(SegmentRuleSet? rules, int? maximumConditions = 50)
    {
        if (rules is null || rules.Conditions is null) return "Rules and conditions are required.";
        if (!Enum.IsDefined(rules.Match)) return "Unknown segment match mode.";
        if (rules.Conditions.Count == 0) return "At least one condition is required.";
        if (maximumConditions is { } max && rules.Conditions.Count > max) return $"At most {max} conditions are supported.";
        if (rules.Conditions.Any(c => c is null || string.IsNullOrWhiteSpace(c.Field))) return "Every condition requires a field.";
        return null;
    }
}
