using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

/// <summary>Shared segment persistence/query behavior, independent of HTTP controllers.</summary>
public static class SegmentQueries
{
    public static readonly JsonSerializerOptions RulesJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static SegmentRuleSet RulesOf(Segment segment) =>
        JsonSerializer.Deserialize<SegmentRuleSet>(segment.RulesJson, RulesJson)
        ?? new SegmentRuleSet(SegmentMatch.All, []);

    // Keep membership deferred: callers compose this into SQL instead of loading IDs.
    public static IQueryable<Guid> ContactIdsQuery(
        HarborDbContext db, Guid workspaceId, Segment segment) =>
        db.Contacts
            .Where(c => c.WorkspaceId == workspaceId)
            .Where(SegmentCompiler.Compile(RulesOf(segment)))
            .Select(c => c.Id);
}
