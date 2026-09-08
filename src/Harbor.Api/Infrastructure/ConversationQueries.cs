using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Infrastructure;

/// <summary>
/// Builds a tenant-scoped read query shared by the list and overview.
/// Null means the requested segment is unavailable in this workspace.
/// Only segment lookup executes here; conversation filtering stays in SQL.
/// </summary>
public sealed class ConversationQueries(HarborDbContext db)
{
    public async Task<IQueryable<Conversation>?> BuildAsync(
        Guid workspaceId, ConversationFilterRequest filter, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var query = db.Conversations.AsNoTracking().Where(c => c.WorkspaceId == workspaceId);

        if (filter.HasSubject is { } hasSubject)
        {
            query = hasSubject
                ? query.Where(c => c.Subject != null && HarborDbContext.TrimCharacters(c.Subject, QueryText.WhitespaceCharacters) != "")
                : query.Where(c => c.Subject == null || HarborDbContext.TrimCharacters(c.Subject, QueryText.WhitespaceCharacters) == "");
        }
        if (filter.AwaitingFirstResponse is { } awaiting)
            query = query.Where(c => (c.FirstRespondedAt == null) == awaiting);
        if (filter.HasTags is { } hasTags)
            query = query.Where(c => c.Tags.Any() == hasTags);
        if (filter.HasSlaTarget is { } hasTarget)
            query = query.Where(c => (c.FirstResponseDueAt != null || c.ResolutionDueAt != null) == hasTarget);
        if (filter.UpdatedFrom is { } updatedFrom)
            query = query.Where(c => c.UpdatedAt >= updatedFrom);
        if (filter.UpdatedBefore is { } updatedBefore)
            query = query.Where(c => c.UpdatedAt < updatedBefore);

        if (filter.State is { } state)
        {
            query = query.Where(c => c.State == state);
        }

        if (filter.InboxId is { } inboxId)
        {
            query = query.Where(c => c.InboxId == inboxId);
        }

        if (filter.ContactId is { } contactId)
        {
            query = query.Where(c => c.ContactId == contactId);
        }

        if (filter.AssignedTeammateId is { } teammateId)
        {
            query = query.Where(c => c.AssignedTeammateId == teammateId);
        }

        if (filter.AssignedTeamId is { } teamId)
        {
            query = query.Where(c => c.AssignedTeamId == teamId);
        }

        if (filter.Unassigned == true)
        {
            query = query.Where(c => c.AssignedTeammateId == null && c.AssignedTeamId == null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var tagName = filter.Tag.Trim().ToLower();
            query = query.Where(c => c.Tags.Any(ct => ct.Tag!.Name.ToLower() == tagName));
        }

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var needle = filter.Q.Trim().ToLower();
            query = query.Where(c =>
                (c.Subject != null && c.Subject.ToLower().Contains(needle))
                || c.Messages.Any(m =>
                    m.Kind == MessageKind.Reply && m.Body.ToLower().Contains(needle)));
        }

        if (filter.Priority is { } priority)
        {
            query = query.Where(c => c.Priority == priority);
        }

        if (filter.Channel is { } channel)
        {
            query = query.Where(c => c.Channel == channel);
        }

        if (filter.SegmentId is { } segmentId)
        {
            var segment = await db.Segments
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == segmentId && s.WorkspaceId == workspaceId, cancellationToken);
            if (segment is null)
            {
                return null;
            }

            // A subquery, not a materialized id list: the segment stays a
            // WHERE clause however many contacts match it.
            var contactIds = SegmentQueries.ContactIdsQuery(db, workspaceId, segment);
            query = query.Where(c => contactIds.Contains(c.ContactId));
        }

        if (filter.SlaBreached == true)
        {
            // Mirrors Conversation.IsSlaBreached, expressed in LINQ so the
            // database does the filtering.
            query = query.Where(c =>
                (c.FirstResponseDueAt != null
                    && (c.FirstRespondedAt == null
                        ? now > c.FirstResponseDueAt
                        : c.FirstRespondedAt > c.FirstResponseDueAt))
                || (c.ResolutionDueAt != null
                    && (c.FirstResolvedAt == null
                        ? now > c.ResolutionDueAt
                        : c.FirstResolvedAt > c.ResolutionDueAt)));
        }

        if (filter.CreatedFrom is { } from)
        {
            query = query.Where(c => c.CreatedAt >= from);
        }

        if (filter.CreatedBefore is { } before)
        {
            query = query.Where(c => c.CreatedAt < before);
        }

        return query;
    }

    // Equal timestamps need a unique tie-breaker for reproducible page boundaries.
    public static IOrderedQueryable<Conversation> Order(
        IQueryable<Conversation> query, ConversationSort sort) => sort switch
    {
        ConversationSort.PriorityFirst => query.OrderByDescending(c => c.Priority)
            .ThenByDescending(c => c.LastMessageAt).ThenBy(c => c.Id),
        ConversationSort.OldestCreated => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
        ConversationSort.NewestCreated => query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
        _ => query.OrderByDescending(c => c.LastMessageAt).ThenBy(c => c.Id),
    };
}
