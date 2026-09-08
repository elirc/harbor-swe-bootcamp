using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

/// <summary>
/// Aggregate reporting over conversations. Every endpoint takes the same
/// <see cref="ReportFilterRequest"/>, so one slice of conversations can be
/// viewed through any report.
///
/// Timings are computed in memory rather than in SQL: durations are
/// differences between DateTimeOffsets that are stored as UTC ticks through a
/// value converter, and SQLite has no percentile function anyway. The slice is
/// materialized once per request and every figure is derived from it, which
/// also keeps the numbers on a page mutually consistent.
/// </summary>
[ApiController]
public class ReportsController(HarborDbContext db) : ControllerBase
{
    [HttpGet("api/workspaces/{workspaceId:guid}/reports/volume")]
    public async Task<ActionResult<VolumeReportResponse>> Volume(
        Guid workspaceId, [FromQuery] ReportFilterRequest filter,
        [FromQuery] ReportInterval interval = ReportInterval.Day)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var conversations = await SliceAsync(workspaceId, filter);
        try { return VolumeReports.Build(conversations, interval); }
        catch (ReportLimitException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            { Status = 422, Title = "Report interval too fine", Detail = ex.Message });
        }
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/reports/response-times")]
    public async Task<ActionResult<ResponseTimeReportResponse>> ResponseTimes(
        Guid workspaceId, [FromQuery] ReportFilterRequest filter)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var conversations = await SliceAsync(workspaceId, filter);
        var now = DateTimeOffset.UtcNow;

        return new ResponseTimeReportResponse(
            conversations.Count,
            conversations.Count(c => c.FirstRespondedAt is null),
            conversations.Count(c => c.FirstResolvedAt is not null),
            conversations.Count(c => c.IsFirstResponseBreached(now)),
            conversations.Count(c => c.IsResolutionBreached(now)),
            Stats(FirstResponseMinutes(conversations)),
            Stats(ResolutionMinutes(conversations)));
    }

    /// <summary>Per-teammate breakdown by assignment; unassigned work is its own row.</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/reports/teammates")]
    public async Task<ActionResult<List<BreakdownRowResponse>>> Teammates(
        Guid workspaceId, [FromQuery] ReportFilterRequest filter)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var conversations = await SliceAsync(workspaceId, filter);
        var names = await db.Teammates
            .Where(t => t.WorkspaceId == workspaceId)
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        return conversations
            .GroupBy(c => c.AssignedTeammateId)
            .Select(g => Row(
                g.Key,
                g.Key is { } id ? names.GetValueOrDefault(id, "(deleted teammate)") : "(unassigned)",
                g.ToList()))
            .OrderByDescending(r => r.Conversations)
            .ThenBy(r => r.Name)
            .ToList();
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/reports/inboxes")]
    public async Task<ActionResult<List<BreakdownRowResponse>>> Inboxes(
        Guid workspaceId, [FromQuery] ReportFilterRequest filter)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var conversations = await SliceAsync(workspaceId, filter);
        var names = await db.Inboxes
            .Where(i => i.WorkspaceId == workspaceId)
            .ToDictionaryAsync(i => i.Id, i => i.Name);

        return conversations
            .GroupBy(c => c.InboxId)
            .Select(g => Row(g.Key, names.GetValueOrDefault(g.Key, "(deleted inbox)"), g.ToList()))
            .OrderByDescending(r => r.Conversations)
            .ThenBy(r => r.Name)
            .ToList();
    }

    /// <summary>
    /// Tag distribution over the slice. Conversations can carry several tags,
    /// so shares are per-tag fractions of the slice and do not sum to 1.
    /// </summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/reports/tags")]
    public async Task<ActionResult<TagDistributionResponse>> Tags(
        Guid workspaceId, [FromQuery] ReportFilterRequest filter)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var conversations = await SliceAsync(workspaceId, filter);
        var total = conversations.Count;

        var rows = conversations
            .SelectMany(c => c.Tags)
            .Where(t => t.Tag is not null)
            .GroupBy(t => new { t.TagId, t.Tag!.Name })
            .Select(g => new TagDistributionRowResponse(
                g.Key.TagId, g.Key.Name, g.Count(),
                total == 0 ? 0 : Math.Round((double)g.Count() / total, 4)))
            .OrderByDescending(r => r.Conversations)
            .ThenBy(r => r.Name)
            .ToList();

        return new TagDistributionResponse(
            total, conversations.Count(c => c.Tags.Count == 0), rows);
    }

    /// <summary>
    /// Materializes the filtered conversations once, with tags, so every
    /// figure in a report is derived from the same snapshot.
    /// </summary>
    private Task<List<Conversation>> SliceAsync(Guid workspaceId, ReportFilterRequest filter) =>
        ReportQueries.Build(db, workspaceId, filter)
            .Include(c => c.Tags).ThenInclude(t => t.Tag).ToListAsync();

    private static BreakdownRowResponse Row(Guid? id, string name, List<Conversation> conversations)
    {
        var now = DateTimeOffset.UtcNow;
        return new BreakdownRowResponse(
            id, name, conversations.Count,
            conversations.Count(c => c.State == ConversationState.Open),
            conversations.Count(c => c.State == ConversationState.Snoozed),
            conversations.Count(c => c.State == ConversationState.Closed),
            conversations.Count(c => c.IsSlaBreached(now)),
            Stats(FirstResponseMinutes(conversations)),
            Stats(ResolutionMinutes(conversations)));
    }

    private static IEnumerable<double> FirstResponseMinutes(IEnumerable<Conversation> conversations) =>
        conversations
            .Where(c => c.FirstRespondedAt is not null)
            .Select(c => (c.FirstRespondedAt!.Value - c.CreatedAt).TotalMinutes);

    private static IEnumerable<double> ResolutionMinutes(IEnumerable<Conversation> conversations) =>
        conversations
            .Where(c => c.FirstResolvedAt is not null)
            .Select(c => (c.FirstResolvedAt!.Value - c.CreatedAt).TotalMinutes);

    private static DurationStatsResponse Stats(IEnumerable<double> minutes)
    {
        var sample = minutes.ToList();
        return new DurationStatsResponse(
            sample.Count,
            Round(Statistics.Percentile(sample, 50)),
            Round(Statistics.Percentile(sample, 90)),
            Round(Statistics.Percentile(sample, 95)),
            Round(Statistics.Average(sample)));
    }

    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, 2);

}
