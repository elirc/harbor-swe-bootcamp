using System.Text.Json;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

public static class ConfigurationAudits
{
    public static void Availability(HarborDbContext db, Guid workspace, Guid actor, Guid teammate,
        TeammateAvailability oldAvailability, int? oldCapacity, TeammateAvailability nextAvailability, int? nextCapacity, DateTimeOffset now)
    {
        var fields = new List<string>();
        if (oldAvailability != nextAvailability) fields.Add("Availability");
        if (oldCapacity != nextCapacity) fields.Add("CapacityLimit");
        if (fields.Count == 0) return;
        Add(db, workspace, actor, teammate, null, ConfigurationEntityKind.TeammateAvailability, ConfigurationAuditAction.Updated, fields,
            JsonSerializer.Serialize(new { OldAvailability = oldAvailability, NewAvailability = nextAvailability, OldCapacityLimit = oldCapacity, NewCapacityLimit = nextCapacity }), now);
    }

    public static void Membership(HarborDbContext db, Guid workspace, Guid actor, Guid team, Guid teammate, bool added, DateTimeOffset now) =>
        Add(db, workspace, actor, team, teammate, ConfigurationEntityKind.TeamMembership,
            added ? ConfigurationAuditAction.Created : ConfigurationAuditAction.Deleted, ["Membership"],
            JsonSerializer.Serialize(new { TeamId = team, TeammateId = teammate, Added = added }), now);

    public static void CannedReply(HarborDbContext db, Guid workspace, Guid actor, Guid id, ConfigurationAuditAction action,
        IReadOnlyList<string> changedFields, DateTimeOffset now)
    {
        var fields = changedFields.Where(f => f is "Shortcut" or "Title" or "Body").Distinct().Order().ToArray();
        if (fields.Length == 0) return;
        Add(db, workspace, actor, id, null, ConfigurationEntityKind.CannedReply, action, fields, null, now);
    }

    private static void Add(HarborDbContext db, Guid workspace, Guid actor, Guid id, Guid? related,
        ConfigurationEntityKind kind, ConfigurationAuditAction action, IReadOnlyList<string> fields, string? values, DateTimeOffset now) =>
        db.ConfigurationAuditEvents.Add(new ConfigurationAuditEvent { WorkspaceId = workspace, ActorTeammateId = actor,
            EntityId = id, RelatedTeammateId = related, EntityKind = kind, Action = action, ChangedFieldsJson = JsonSerializer.Serialize(fields),
            AllowedChangesJson = values, OccurredAt = now });
}
