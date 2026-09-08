using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public static class ReplyNotifications
{
    public static async Task StageAsync(HarborDbContext db, Conversation conversation, Message message,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (message.AuthorType != AuthorType.Contact || message.Kind != MessageKind.Reply) return;
        var recipients = await db.ConversationWatchers.Where(w => w.ConversationId == conversation.Id
            && w.WorkspaceId == conversation.WorkspaceId && w.Teammate!.WorkspaceId == conversation.WorkspaceId)
            .Select(w => w.TeammateId).ToListAsync(ct);
        foreach (var teammateId in recipients) db.ReplyNotifications.Add(new ReplyNotification
        {
            WorkspaceId = conversation.WorkspaceId, TeammateId = teammateId, ConversationId = conversation.Id,
            MessageId = message.Id, CreatedAt = now,
        });
    }
}
