using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public record RoutingSkip(Guid? RuleId, string Reason);
public record RoutingDecision(Guid? VersionId, Guid? RuleId, Guid? TeamId, IReadOnlyList<RoutingSkip> Skipped);

public static class InboxRouting
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public static string? ShapeError(IReadOnlyList<RoutingRule>? rules)
    {
        if (rules is null || rules.Count > 20) return "Supply an array of at most 20 rules.";
        var ids = new HashSet<Guid>();
        foreach (var rule in rules)
        {
            if (rule is null || rule.Id == Guid.Empty || !ids.Add(rule.Id) || rule.TargetTeamId == Guid.Empty)
                return "Rule IDs must be nonempty and distinct, and target IDs must be nonempty.";
            if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 100) return "Rule names must contain 1-100 characters.";
            if ((rule.Priority is { } priority && !Enum.IsDefined(priority)) || (rule.Channel is { } channel && !Enum.IsDefined(channel)))
                return "Unknown priority or channel.";
            if (SegmentRuleShape.Error(rule.Conditions, 10) is { } error) return error;
        }
        return null;
    }

    public static async Task<RoutingDecision> EvaluateAsync(HarborDbContext db, Guid workspaceId, Guid inboxId,
        Guid contactId, ConversationPriority priority, MessageChannel channel, CancellationToken ct = default)
    {
        var definition = await db.InboxRoutingDefinitions.AsNoTracking().SingleOrDefaultAsync(d => d.InboxId == inboxId && d.WorkspaceId == workspaceId, ct);
        if (definition is null) return new(null, null, null, []);
        var version = await db.InboxRoutingVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == definition.ActiveVersionId
            && v.InboxId == inboxId && v.WorkspaceId == workspaceId, ct);
        List<RoutingRule>? rules;
        try { rules = version?.SchemaVersion == 1 ? JsonSerializer.Deserialize<List<RoutingRule>>(version.RulesJson, Json) : null; }
        catch (JsonException) { rules = null; }
        if (ShapeError(rules) is not null) return new(definition.ActiveVersionId, null, null, [new(null, "InvalidDefinition")]);
        var teamIds = rules!.Select(r => r.TargetTeamId).Distinct().ToArray();
        var available = (await db.Teams.Where(t => t.WorkspaceId == workspaceId && teamIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var skipped = new List<RoutingSkip>();
        foreach (var rule in rules!)
        {
            if (!available.Contains(rule.TargetTeamId)) { skipped.Add(new(rule.Id, "UnavailableTeam")); continue; }
            if ((rule.Priority is { } p && p != priority) || (rule.Channel is { } ch && ch != channel))
            { skipped.Add(new(rule.Id, "ConstraintMismatch")); continue; }
            try
            {
                if (await db.Contacts.Where(c => c.WorkspaceId == workspaceId && c.Id == contactId)
                    .Where(SegmentCompiler.Compile(rule.Conditions)).AnyAsync(ct))
                    return new(version!.Id, rule.Id, rule.TargetTeamId, skipped);
                skipped.Add(new(rule.Id, "ContactDidNotMatch"));
            }
            catch (SegmentRuleException) { skipped.Add(new(rule.Id, "InvalidConditions")); }
        }
        return new(version!.Id, null, null, skipped);
    }
}
