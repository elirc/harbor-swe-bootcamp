using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

/// <summary>
/// Resolves which SLA policy governs a conversation and stamps its targets.
/// </summary>
public static class SlaPolicies
{
    public record TargetDescription(string Source, Guid? PolicyId, string? PolicyName,
        int? FirstResponseMinutes, int? ResolutionMinutes, Guid? WorkingCalendarId = null);
    public record CalculatedTargets(TargetDescription Description, DateTimeOffset? FirstResponseDueAt, DateTimeOffset? ResolutionDueAt);

    public static TargetDescription Describe(IEnumerable<SlaPolicy> policies, Inbox inbox, ConversationPriority priority)
    {
        var policy = Resolve(policies, inbox.Id, priority);
        return policy is not null
            ? new TargetDescription("Policy", policy.Id, policy.Name, policy.FirstResponseMinutes, policy.ResolutionMinutes, policy.WorkingCalendarId)
            : new TargetDescription(inbox.FirstResponseSlaMinutes is null ? "None" : "InboxFallback",
                null, null, inbox.FirstResponseSlaMinutes, null);
    }
    /// <summary>
    /// Picks the most specific policy covering the conversation's inbox and
    /// priority. Ties on specificity are broken by creation order, so the
    /// oldest policy wins and resolution stays deterministic.
    /// </summary>
    public static SlaPolicy? Resolve(
        IEnumerable<SlaPolicy> policies, Guid inboxId, ConversationPriority priority) =>
        policies
            .Where(p => p.Matches(inboxId, priority))
            .OrderByDescending(p => p.Specificity)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .FirstOrDefault();

    /// <summary>
    /// Stamps the conversation's SLA targets from the governing policy. Falls
    /// back to the inbox's first-response minutes when no policy matches, which
    /// keeps inboxes configured before SLA policies existed working unchanged.
    /// </summary>
    public static async Task ApplyAsync(HarborDbContext db, Conversation conversation, Inbox inbox)
    {
        var policies = await db.SlaPolicies
            .Where(p => p.WorkspaceId == conversation.WorkspaceId)
            .ToListAsync();

        var targets = await CalculateAsync(db, policies, inbox, conversation.Priority, conversation.CreatedAt);
        Stamp(conversation, targets);
    }

    public static async Task<CalculatedTargets> CalculateAsync(HarborDbContext db, IEnumerable<SlaPolicy> policies,
        Inbox inbox, ConversationPriority priority, DateTimeOffset createdAt)
    {
        var description = Describe(policies, inbox, priority);
        WorkingCalendarDefinition? calendar = null;
        if (description.WorkingCalendarId is { } id)
        {
            var row = await db.WorkingCalendars.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == inbox.WorkspaceId);
            if (row is null || row.SchemaVersion != 1) throw new DomainException("The configured working calendar is unavailable.");
            try { calendar = System.Text.Json.JsonSerializer.Deserialize<WorkingCalendarDefinition>(row.DefinitionJson); }
            catch (System.Text.Json.JsonException) { throw new DomainException("The configured working calendar cannot be read."); }
            if (calendar is null) throw new DomainException("The configured working calendar cannot be read.");
        }
        DateTimeOffset? Deadline(int? minutes)
        {
            if (minutes is null) return null;
            try { return calendar is null ? createdAt.AddMinutes(minutes.Value) : WorkingTime.AddWorkingDuration(createdAt, minutes.Value, calendar); }
            catch (ArgumentOutOfRangeException) { throw new DomainException("SLA target exceeds the supported timestamp range."); }
        }
        return new(description, Deadline(description.FirstResponseMinutes), Deadline(description.ResolutionMinutes));
    }

    public static void Stamp(Conversation conversation, CalculatedTargets targets)
    {
        conversation.FirstResponseDueAt = targets.FirstResponseDueAt;
        conversation.ResolutionDueAt = targets.ResolutionDueAt;
        conversation.SlaPolicyId = targets.Description.PolicyId;
        conversation.AppliedWorkingCalendarId = targets.Description.WorkingCalendarId;
    }
}
