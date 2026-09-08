using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Api.Infrastructure;

/// <summary>Post-validation reply/note effects, staged for one caller-owned commit.</summary>
public static class MessageAppending
{
    public static async Task StageAsync(HarborDbContext db, Conversation conversation, Message message, DateTimeOffset now,
        bool preserveInboundEmailIdentity = false)
    {
        if (conversation.Channel == MessageChannel.Email && message.Kind == MessageKind.Reply)
        {
            message.Channel = MessageChannel.Email;
            if (!preserveInboundEmailIdentity) message.EmailMessageId = EmailRendering.MessageIdFor(message.Id);
        }
        message.CreatedAt = now;
        var previousState = conversation.State;
        conversation.RegisterMessage(message, now);
        WorkspaceEvents.Message(db, conversation, message, now);
        WorkspaceEvents.State(db, conversation, previousState, now);
        db.Messages.Add(message);
        await ReplyNotifications.StageAsync(db, conversation, message, now);
        await SlaBreaches.DetectAsync(db, conversation, now);
        await Webhooks.PublishAsync(db, conversation.WorkspaceId, WebhookEventType.MessageCreated, message.ToResponse(), now);
    }
}
