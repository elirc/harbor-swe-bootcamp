using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

/// <summary>Stages the complete priority workflow; the caller owns the commit.</summary>
public static class PriorityChanges
{
    public static async Task ApplyAsync(HarborDbContext db, Conversation conversation, Inbox inbox,
        ConversationPriority priority, DateTimeOffset now)
    {
        conversation.SetPriority(priority, now);
        await SlaPolicies.ApplyAsync(db, conversation, inbox);
        await SlaBreaches.DetectAsync(db, conversation, now);
    }
}
