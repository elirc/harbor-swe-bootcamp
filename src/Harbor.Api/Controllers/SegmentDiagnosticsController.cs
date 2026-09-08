using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class SegmentDiagnosticsController(HarborDbContext db) : ControllerBase
{
    private ObjectResult InvalidRules(string error) => UnprocessableEntity(new ProblemDetails
    { Status = 422, Title = "Invalid segment rules", Detail = error });

    private static (SegmentRuleSet? Rules, string? Error) ReadRules(Segment segment, int? maximumConditions)
    {
        try
        {
            var rules = SegmentQueries.RulesOf(segment);
            if (SegmentRuleShape.Error(rules, maximumConditions) is { } error) return (null, error);
            SegmentCompiler.Validate(rules);
            return (rules, null);
        }
        catch (JsonException) { return (null, "The stored rule document is invalid JSON."); }
        catch (SegmentRuleException ex) { return (null, ex.Message); }
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/segments/preview")]
    public async Task<ActionResult<SegmentPreviewResponse>> Preview(Guid workspaceId, PreviewSegmentRequest request, CancellationToken ct)
    {
        try
        {
            var predicate = SegmentCompiler.Compile(request.Rules);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var query = db.Contacts.AsNoTracking().Where(c => c.WorkspaceId == workspaceId).Where(predicate);
            var total = await query.CountAsync(ct);
            var contacts = await query.OrderBy(c => c.Name).ThenBy(c => c.Id).Take(request.SampleLimit).ToListAsync(ct);
            await transaction.CommitAsync(ct);
            return new SegmentPreviewResponse(total, contacts.Select(c => c.ToResponse()).ToList());
        }
        catch (SegmentRuleException ex) { return InvalidRules(ex.Message); }
    }

    [HttpPost("api/segments/{id:guid}/clone")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SegmentResponse>> Clone(Guid id, CloneSegmentRequest request, CancellationToken ct)
    {
        var source = await db.Segments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.WorkspaceId == User.GetWorkspaceId(), ct);
        if (source is null) return NotFound();
        var definition = ReadRules(source, null);
        if (definition.Error is { } error) return InvalidRules(error);
        var name = request.Name.Trim();
        if (await db.Segments.AnyAsync(s => s.WorkspaceId == source.WorkspaceId && s.Name == name, ct)) return NameConflict();
        var clone = new Segment { WorkspaceId = source.WorkspaceId, Name = name,
            RulesJson = JsonSerializer.Serialize(definition.Rules, SegmentQueries.RulesJson) };
        db.Segments.Add(clone);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            db.Entry(clone).State = EntityState.Detached;
            if (!await db.Segments.AnyAsync(s => s.WorkspaceId == source.WorkspaceId && s.Name == name, ct)) throw;
            return NameConflict();
        }
        return CreatedAtAction("GetById", "Segments", new { id = clone.Id }, clone.ToResponse(definition.Rules!));
    }

    private ObjectResult NameConflict() => Conflict(new ProblemDetails { Status = 409, Title = "Duplicate segment name" });

    [HttpGet("api/segments/{id:guid}/contacts/{contactId:guid}/explanation")]
    public async Task<ActionResult<SegmentExplanationResponse>> Explain(Guid id, Guid contactId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var workspaceId = User.GetWorkspaceId();
        var segment = await db.Segments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.WorkspaceId == workspaceId, ct);
        var contact = db.Contacts.AsNoTracking().Where(c => c.Id == contactId && c.WorkspaceId == workspaceId);
        if (segment is null || !await contact.AnyAsync(ct)) return NotFound();
        var definition = ReadRules(segment, 50);
        if (definition.Error is { } error) return InvalidRules(error);
        var rules = definition.Rules!;
        var results = new List<SegmentConditionResult>();
        for (var i = 0; i < rules.Conditions.Count; i++)
        {
            var condition = rules.Conditions[i];
            var predicate = SegmentCompiler.Compile(new SegmentRuleSet(SegmentMatch.All, [condition]));
            var matches = await contact.AnyAsync(predicate, ct);
            results.Add(new SegmentConditionResult(i, condition.Field, condition.Operator, condition.Value, matches));
        }
        var overall = await contact.AnyAsync(SegmentCompiler.Compile(rules), ct);
        await transaction.CommitAsync(ct);
        return new SegmentExplanationResponse(id, contactId, rules.Match, overall, results);
    }
}
