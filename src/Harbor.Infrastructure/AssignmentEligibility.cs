using Harbor.Domain;

namespace Harbor.Infrastructure;

public record AssignmentLoad
{
    public Guid TeammateId { get; init; }
    public int Count { get; init; }
}
public record EligibilityDecision(bool Away, bool AtCapacity)
{
    public bool Eligible => !Away && !AtCapacity;
}

public static class AssignmentEligibility
{
    public static EligibilityDecision Evaluate(TeammateAvailability availability, int? capacity, int count) =>
        new(availability != TeammateAvailability.Available, capacity is { } limit && count >= limit);

    public static IQueryable<AssignmentLoad> Counts(HarborDbContext db, Guid workspaceId) =>
        db.Conversations.Where(c => c.WorkspaceId == workspaceId && c.AssignedTeammateId != null
            && c.State != ConversationState.Closed)
            .GroupBy(c => c.AssignedTeammateId!.Value).Select(g => new AssignmentLoad { TeammateId = g.Key, Count = g.Count() });
}
