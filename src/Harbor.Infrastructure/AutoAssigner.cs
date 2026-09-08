using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

/// <summary>
/// Round-robin auto-assignment for inboxes with <see cref="Inbox.AutoAssign"/>.
/// Eligible teammates are Available and under their capacity limit (open
/// and snoozed conversations). The rotation pointer is
/// <see cref="Inbox.LastAssignedTeammateId"/>.
/// </summary>
public static class AutoAssigner
{
    /// <summary>
    /// Picks the next eligible teammate for the inbox and advances the
    /// rotation pointer. Returns null when nobody is eligible.
    /// </summary>
    public static async Task<Teammate?> PickNextAsync(HarborDbContext db, Inbox inbox)
    {
        var candidates = await db.Teammates
            .Where(t => t.WorkspaceId == inbox.WorkspaceId
                && t.Availability == TeammateAvailability.Available)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToListAsync();

        if (candidates.Count == 0)
        {
            return null;
        }

        var openCounts = await AssignmentEligibility.Counts(db, inbox.WorkspaceId)
            .ToDictionaryAsync(g => g.TeammateId, g => g.Count);

        var eligible = candidates
            .Where(t => AssignmentEligibility.Evaluate(t.Availability, t.CapacityLimit, openCounts.GetValueOrDefault(t.Id)).Eligible)
            .ToList();
        if (eligible.Count == 0)
        {
            return null;
        }

        // Continue the rotation after the previous assignee (which may itself
        // have become ineligible); wrap around when the pointer is at the end.
        var startIndex = 0;
        var lastIndex = candidates.FindIndex(t => t.Id == inbox.LastAssignedTeammateId);
        if (lastIndex >= 0)
        {
            var next = candidates
                .Skip(lastIndex + 1)
                .FirstOrDefault(eligible.Contains);
            if (next is not null)
            {
                startIndex = eligible.IndexOf(next);
            }
        }

        var picked = eligible[startIndex];
        inbox.LastAssignedTeammateId = picked.Id;
        return picked;
    }
}
