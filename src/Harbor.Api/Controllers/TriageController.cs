using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class TriageController(HarborDbContext db, SnoozeWake wake, TimeProvider clock) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/conversations/bulk-priority")]
    public async Task<ActionResult<List<ConversationSummaryResponse>>> BulkPriority(Guid workspaceId, BulkPriorityRequest request, CancellationToken ct)
    {
        var rows = await db.Conversations.Where(c => c.WorkspaceId == workspaceId && request.ConversationIds.Contains(c.Id))
            .Include(c => c.Inbox).Include(c => c.Tags).ThenInclude(t => t.Tag).OrderBy(c => c.Id).ToListAsync(ct);
        if (rows.Count != request.ConversationIds.Count)
            return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable conversations", Detail = "Every selected conversation must exist in this workspace. Nothing was changed." });
        var now = clock.GetUtcNow();
        foreach (var row in rows) await PriorityChanges.ApplyAsync(db, row, row.Inbox!, request.Priority!.Value, now);
        await db.SaveChangesAsync(ct);
        return rows.Select(c => c.ToSummaryResponse()).ToList();
    }

    [HttpPut("api/conversations/{id:guid}/tags")]
    public async Task<ActionResult<ConversationSummaryResponse>> ReplaceTags(Guid id, ReplaceTagsRequest request, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var conversation = await db.Conversations.Include(c => c.Tags).ThenInclude(t => t.Tag)
                .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), ct);
            if (conversation is null) return NotFound();
            var tags = await db.Tags.Where(t => t.WorkspaceId == conversation.WorkspaceId && request.TagIds.Contains(t.Id)).ToListAsync(ct);
            if (tags.Count != request.TagIds.Count)
                return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable tags", Detail = "Every selected tag must exist in this workspace. Nothing was changed." });
            var wanted = request.TagIds.ToHashSet();
            var current = conversation.Tags.Select(t => t.TagId).ToHashSet();
            foreach (var link in conversation.Tags.Where(t => !wanted.Contains(t.TagId)).ToList())
            {
                db.ConversationTags.Remove(link);
                conversation.Tags.Remove(link);
            }
            foreach (var tag in tags.Where(t => !current.Contains(t.Id)))
            {
                var link = new ConversationTag { ConversationId = id, TagId = tag.Id, Tag = tag };
                conversation.Tags.Add(link);
                db.ConversationTags.Add(link);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return conversation.ToSummaryResponse();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        { return TagConflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sql
            && (sql.SqliteErrorCode is 5 or 6 || sql.SqliteExtendedErrorCode is 1555 or 2067))
        { return TagConflict(); }
    }

    private ObjectResult TagConflict() => Conflict(new ProblemDetails
    { Status = 409, Title = "Tag replacement conflicted", Detail = "Reload the current tags before retrying the complete replacement." });

    [HttpPost("api/workspaces/{workspaceId:guid}/conversations/wake-due")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<WakeDueResponse>> WakeDue(Guid workspaceId, [FromQuery, Range(1, 200)] int limit = 50, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return new WakeDueResponse(await wake.WakeAsync(workspaceId, limit, now, ct), now);
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/assignment-eligibility")]
    public async Task<ActionResult<List<EligibilityResponse>>> Eligibility(Guid workspaceId, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        var teammates = await db.Teammates.AsNoTracking().Where(t => t.WorkspaceId == workspaceId)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToPageAsync(paging, Response, ct);
        var ids = teammates.Select(t => t.Id).ToArray();
        var counts = await AssignmentEligibility.Counts(db, workspaceId).Where(c => ids.Contains(c.TeammateId))
            .ToDictionaryAsync(c => c.TeammateId, c => c.Count, ct);
        return teammates.Select(t =>
        {
            var count = counts.GetValueOrDefault(t.Id);
            var decision = AssignmentEligibility.Evaluate(t.Availability, t.CapacityLimit, count);
            var reasons = new List<string>();
            if (decision.Away) reasons.Add("Away");
            if (decision.AtCapacity) reasons.Add("AtCapacity");
            return new EligibilityResponse(t.Id, t.Name, t.Availability, count, t.CapacityLimit, decision.Eligible, reasons);
        }).ToList();
    }
}
