using System.Text.Json;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public static class WorkspaceEvents
{
    public static void Created(HarborDbContext db, Conversation conversation, DateTimeOffset now)
    {
        Add(db, conversation, WorkspaceEventKind.ConversationCreated, conversation.Id, new { conversation.InboxId, conversation.ContactId, conversation.Channel }, now);
        foreach (var message in conversation.Messages) Message(db, conversation, message, now);
        if (conversation.AssignedTeammateId is not null || conversation.AssignedTeamId is not null) Assignment(db, conversation, now);
    }
    public static void Message(HarborDbContext db, Conversation conversation, Message message, DateTimeOffset now) =>
        Add(db, conversation, WorkspaceEventKind.MessageCreated, message.Id, new { message.Kind, message.AuthorType, message.AuthorContactId, message.AuthorTeammateId }, now);
    public static void State(HarborDbContext db, Conversation conversation, ConversationState previous, DateTimeOffset now)
    {
        if (conversation.State != previous) Add(db, conversation, WorkspaceEventKind.StateChanged, conversation.Id, new { PreviousState = previous, conversation.State }, now);
    }
    public static void Assignment(HarborDbContext db, Conversation conversation, DateTimeOffset now) =>
        Add(db, conversation, WorkspaceEventKind.AssignmentChanged, conversation.Id, new { conversation.AssignedTeammateId, conversation.AssignedTeamId }, now);

    private static void Add(HarborDbContext db, Conversation conversation, WorkspaceEventKind kind, Guid entity, object safeMetadata, DateTimeOffset now) =>
        db.WorkspaceEvents.Add(new WorkspaceEvent { WorkspaceId = conversation.WorkspaceId, ConversationId = conversation.Id,
            Kind = kind, EntityId = entity, MetadataJson = JsonSerializer.Serialize(safeMetadata), RecordedAt = now });

    public static async Task<int> PruneAsync(HarborDbContext db, Guid workspace, DateTimeOffset now, CancellationToken ct) =>
        await WriteTransactions.RunAsync(db, async () =>
        {
            var first = await db.WorkspaceEvents.Where(e => e.WorkspaceId == workspace).OrderBy(e => e.Sequence).Take(1000).ToListAsync(ct);
            var expiredPrefix = first.TakeWhile(e => e.RecordedAt < now.AddDays(-7)).ToList();
            if (expiredPrefix.Count == 0) return 0;
            var watermark = await db.WorkspaceEventWatermarks.SingleOrDefaultAsync(w => w.WorkspaceId == workspace, ct);
            if (watermark is null) { watermark = new WorkspaceEventWatermark { WorkspaceId = workspace }; db.WorkspaceEventWatermarks.Add(watermark); }
            watermark.PurgedThrough = Math.Max(watermark.PurgedThrough, expiredPrefix[^1].Sequence);
            db.WorkspaceEvents.RemoveRange(expiredPrefix);
            await db.SaveChangesAsync(ct);
            return expiredPrefix.Count;
        }, ct);
}
