using Harbor.Domain;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public sealed class SnoozeWake(HarborDbContext db)
{
    public async Task<IReadOnlyList<Guid>> WakeAsync(Guid workspaceId, int limit, DateTimeOffset now, CancellationToken ct = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var due = await db.Conversations.Where(c => c.WorkspaceId == workspaceId
                && c.State == ConversationState.Snoozed && c.SnoozedUntil != null && c.SnoozedUntil <= now)
            .OrderBy(c => c.SnoozedUntil).ThenBy(c => c.Id).Take(limit).ToListAsync(ct);
        foreach (var conversation in due)
        {
            var previous = conversation.State;
            conversation.Open(now);
            WorkspaceEvents.State(db, conversation, previous, now);
        }
        await db.SaveChangesAsync(ct);
        return due.Select(c => c.Id).ToList();
    }
}
