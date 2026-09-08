using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Infrastructure;

/// <summary>
/// Everything that must happen when a conversation begins, in one place:
/// stamp SLA targets, round-robin auto-assign, record the assignment, and
/// queue the webhooks.
///
/// This exists so every channel a conversation can arrive on gets identical
/// treatment. Chat and inbound email both come through here, which is what
/// stops email conversations from quietly skipping auto-assignment or SLA
/// because a second code path forgot to call something.
/// </summary>
public static class ConversationStarter
{
    /// <summary>
    /// Builds the conversation and its opening contact message, applies the
    /// start-time rules, and queues events. The caller still SaveChanges, so
    /// the conversation, its assignment, and its events commit together.
    /// </summary>
    public static async Task<Conversation> StartAsync(
        HarborDbContext db, Inbox inbox, Contact contact, string? subject, string body,
        DateTimeOffset now, ConversationPriority priority = ConversationPriority.Normal,
        MessageChannel channel = MessageChannel.Chat, string? emailMessageId = null)
    {
        InboxLifecycle.RequireActiveForNewWork(inbox);
        var conversation = new Conversation
        {
            WorkspaceId = inbox.WorkspaceId,
            InboxId = inbox.Id,
            ContactId = contact.Id,
            Subject = subject,
            Priority = priority,
            Channel = channel,
            CreatedAt = now,
            UpdatedAt = now,
            LastMessageAt = now,
        };
        await SlaPolicies.ApplyAsync(db, conversation, inbox);

        conversation.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            AuthorType = AuthorType.Contact,
            AuthorContactId = contact.Id,
            Body = body,
            Channel = channel,
            EmailMessageId = emailMessageId,
            CreatedAt = now,
        });
        contact.LastSeenAt = now;

        // SQL segment predicates cannot see an unsaved email contact. Persist it
        // inside the caller's still-uncommitted transaction before evaluating routing.
        if (db.Entry(contact).State == EntityState.Added
            && await db.InboxRoutingDefinitions.AnyAsync(d => d.InboxId == inbox.Id && d.WorkspaceId == inbox.WorkspaceId))
        {
            if (db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Routing a new contact requires a caller-owned transaction.");
            await db.SaveChangesAsync();
        }
        var routing = await InboxRouting.EvaluateAsync(db, inbox.WorkspaceId, inbox.Id, contact.Id, priority, channel);
        if (routing.TeamId is { } teamId)
        {
            conversation.AssignToTeam(teamId, now);
            db.AssignmentEvents.Add(new AssignmentEvent
            {
                ConversationId = conversation.Id, Kind = AssignmentKind.Routing, ToTeamId = teamId,
                RoutingVersionId = routing.VersionId, RoutingRuleId = routing.RuleId, CreatedAt = now,
            });
        }
        else if (inbox.AutoAssign && await AutoAssigner.PickNextAsync(db, inbox) is { } assignee)
        {
            conversation.AssignToTeammate(assignee.Id, now);
            db.AssignmentEvents.Add(new AssignmentEvent
            {
                ConversationId = conversation.Id,
                Kind = AssignmentKind.Auto,
                ToTeammateId = assignee.Id,
                CreatedAt = now,
            });
        }

        db.Conversations.Add(conversation);

        // Queued in the same SaveChanges as the conversation itself, so the
        // event cannot outlive a rolled-back write or be lost after a commit.
        await Webhooks.PublishAsync(
            db, inbox.WorkspaceId, WebhookEventType.ConversationCreated,
            conversation.ToSummaryResponse(), now);
        if (conversation.AssignedTeammateId is not null || conversation.AssignedTeamId is not null)
        {
            await Webhooks.PublishAsync(
                db, inbox.WorkspaceId, WebhookEventType.ConversationAssigned,
                conversation.ToSummaryResponse(), now);
        }

        WorkspaceEvents.Created(db, conversation, now);
        return conversation;
    }
}
