using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ReportInsightsController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private static bool InvalidRange(ReportFilterRequest filter) => filter.From is { } from && filter.To is { } to && from >= to;
    private ObjectResult RangeError() => BadRequest(new ProblemDetails { Status = 400, Title = "From must be earlier than To" });

    [HttpGet("api/workspaces/{workspaceId:guid}/reports/backlog-age")]
    public async Task<ActionResult<BacklogAgeResponse>> BacklogAge(Guid workspaceId, [FromQuery] ReportFilterRequest filter, CancellationToken ct)
    {
        if (InvalidRange(filter)) return RangeError();
        var now = clock.GetUtcNow();
        var dayAgo = now.AddHours(-24);
        var threeDaysAgo = now.AddHours(-72);
        var counts = await ReportQueries.Build(db, workspaceId, filter)
            .Where(c => c.State == ConversationState.Open || c.State == ConversationState.Snoozed)
            .GroupBy(c => 1).Select(g => new BacklogAgeResponse(now, g.Count(),
                g.Count(c => c.State == ConversationState.Open), g.Count(c => c.State == ConversationState.Snoozed),
                g.Count(c => c.CreatedAt > dayAgo),
                g.Count(c => c.CreatedAt <= dayAgo && c.CreatedAt > threeDaysAgo),
                g.Count(c => c.CreatedAt <= threeDaysAgo))).SingleOrDefaultAsync(ct);
        return counts ?? new BacklogAgeResponse(now, 0, 0, 0, 0, 0, 0);
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/reports/team-workload")]
    public async Task<ActionResult<List<TeamWorkloadResponse>>> TeamWorkload(Guid workspaceId,
        [FromQuery] ReportFilterRequest filter, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var teams = await db.Teams.AsNoTracking().Where(t => t.WorkspaceId == workspaceId)
            .OrderBy(t => t.Name).ThenBy(t => t.Id).ToPageAsync(paging, Response, ct);
        var ids = teams.Select(t => t.Id).ToArray();
        var workload = await ReportQueries.Build(db, workspaceId, filter)
            .Where(c => c.AssignedTeamId != null && ids.Contains(c.AssignedTeamId.Value))
            .GroupBy(c => c.AssignedTeamId!.Value).Select(g => new
            {
                Id = g.Key, Total = g.Count(), Open = g.Count(c => c.State == ConversationState.Open),
                Snoozed = g.Count(c => c.State == ConversationState.Snoozed), Closed = g.Count(c => c.State == ConversationState.Closed),
            }).ToDictionaryAsync(g => g.Id, ct);
        var roster = await db.TeamMemberships.Where(m => ids.Contains(m.TeamId) && m.Teammate!.WorkspaceId == workspaceId)
            .GroupBy(m => m.TeamId).Select(g => new
            { Id = g.Key, Total = g.Count(), Available = g.Count(m => m.Teammate!.Availability == TeammateAvailability.Available) })
            .ToDictionaryAsync(g => g.Id, ct);
        var rows = teams.Select(t =>
        {
            workload.TryGetValue(t.Id, out var work);
            roster.TryGetValue(t.Id, out var members);
            return new TeamWorkloadResponse(t.Id, t.Name, members?.Total ?? 0, members?.Available ?? 0,
                work?.Total ?? 0, work?.Open ?? 0, work?.Snoozed ?? 0, work?.Closed ?? 0);
        }).ToList();
        await transaction.CommitAsync(ct);
        return rows;
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/reports/volume.csv")]
    public async Task<IActionResult> VolumeCsv(Guid workspaceId, [FromQuery] ReportFilterRequest filter,
        [FromQuery, EnumDataType(typeof(ReportInterval))] ReportInterval interval = ReportInterval.Day, CancellationToken ct = default)
    {
        if (InvalidRange(filter)) return RangeError();
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        var conversations = await ReportQueries.Build(db, workspaceId, filter).ToListAsync(ct);
        try
        {
            return File(VolumeReports.Csv(VolumeReports.Build(conversations, interval)),
                "text/csv; charset=utf-8", "conversation-volume.csv");
        }
        catch (ReportLimitException ex)
        { return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Report interval too fine", Detail = ex.Message }); }
    }
}
