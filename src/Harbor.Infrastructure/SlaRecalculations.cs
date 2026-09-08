using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public sealed record SlaRecalculationItem(Guid ConversationId, Guid ConversationVersion, ConversationPriority Priority,
    Guid? OldPolicyId, Guid? OldWorkingCalendarId, DateTimeOffset? OldFirstResponseDueAt, DateTimeOffset? OldResolutionDueAt,
    SlaPolicies.CalculatedTargets Proposed);
public sealed record SlaRecalculationPreview(string Fingerprint, IReadOnlyList<SlaRecalculationItem> Items);

public static class SlaRecalculations
{
    /// <summary>Caller holds a transaction so selected state and configuration form one consistent plan.</summary>
    public static async Task<SlaRecalculationPreview> BuildAsync(HarborDbContext db, Guid workspaceId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count is < 1 or > 100 || ids.Distinct().Count() != ids.Count || ids.Contains(Guid.Empty))
            throw new DomainException("Select 1–100 distinct available unfinished conversations.");
        var conversations = await db.Conversations.AsNoTracking().Where(c => c.WorkspaceId == workspaceId && ids.Contains(c.Id)).ToListAsync(ct);
        if (conversations.Count != ids.Count || conversations.Any(c => c.State == ConversationState.Closed))
            throw new DomainException("Select 1–100 distinct available unfinished conversations.");
        var inboxIds = conversations.Select(c => c.InboxId).Distinct().ToArray();
        var inboxes = await db.Inboxes.AsNoTracking().Where(i => i.WorkspaceId == workspaceId && inboxIds.Contains(i.Id)).ToListAsync(ct);
        if (inboxes.Count != inboxIds.Length) throw new DomainException("A selected inbox is unavailable.");
        var policies = await db.SlaPolicies.AsNoTracking().Where(p => p.WorkspaceId == workspaceId).OrderBy(p => p.Id).ToListAsync(ct);
        var calendarIds = policies.Where(p => p.WorkingCalendarId != null).Select(p => p.WorkingCalendarId!.Value).Distinct().ToArray();
        var calendars = await db.WorkingCalendars.AsNoTracking().Where(c => c.WorkspaceId == workspaceId && calendarIds.Contains(c.Id)).OrderBy(c => c.Id).ToListAsync(ct);
        var items = new List<SlaRecalculationItem>();
        foreach (var id in ids)
        {
            var conversation = conversations.Single(c => c.Id == id);
            var targets = await SlaPolicies.CalculateAsync(db, policies, inboxes.Single(i => i.Id == conversation.InboxId), conversation.Priority, conversation.CreatedAt);
            items.Add(new(id, conversation.Version, conversation.Priority, conversation.SlaPolicyId, conversation.AppliedWorkingCalendarId,
                conversation.FirstResponseDueAt, conversation.ResolutionDueAt, targets));
        }
        var canonical = JsonSerializer.Serialize(new
        {
            Schema = 1, WorkspaceId = workspaceId, Items = items,
            Conversations = conversations.OrderBy(c => c.Id).Select(c => new { c.Id, c.Version, c.CreatedAt, c.State, c.InboxId, c.Priority }),
            Inboxes = inboxes.OrderBy(i => i.Id).Select(i => new { i.Id, i.FirstResponseSlaMinutes }),
            Policies = policies.Select(p => new { p.Id, p.InboxId, p.Priority, p.Name, p.CreatedAt, p.FirstResponseMinutes, p.ResolutionMinutes, p.WorkingCalendarId }),
            Calendars = calendars.Select(c => new { c.Id, c.SchemaVersion, c.DefinitionJson }),
        });
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), items);
    }
}
