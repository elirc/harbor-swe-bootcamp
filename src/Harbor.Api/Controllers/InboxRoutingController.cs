using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public class InboxRoutingController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private Task<bool> Parent(Guid id, CancellationToken ct) => db.Inboxes.AnyAsync(i => i.Id == id && i.WorkspaceId == User.GetWorkspaceId(), ct);

    [HttpGet("api/inboxes/{id:guid}/routing-rules")]
    public async Task<ActionResult<RoutingRulesResponse>> Get(Guid id, CancellationToken ct)
    {
        if (!await Parent(id, ct)) return NotFound();
        var definition = await db.InboxRoutingDefinitions.AsNoTracking().SingleOrDefaultAsync(d => d.InboxId == id && d.WorkspaceId == User.GetWorkspaceId(), ct);
        if (definition is null) return new RoutingRulesResponse(id, null, null, []);
        var version = await db.InboxRoutingVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == definition.ActiveVersionId
            && v.InboxId == id && v.WorkspaceId == User.GetWorkspaceId(), ct);
        if (version?.SchemaVersion != 1) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable routing version" });
        try { return new RoutingRulesResponse(id, definition.Version, version.Id, JsonSerializer.Deserialize<List<RoutingRule>>(version.RulesJson, InboxRouting.Json)!); }
        catch (JsonException) { return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unreadable routing definition" }); }
    }

    [HttpPut("api/inboxes/{id:guid}/routing-rules")]
    public async Task<ActionResult<RoutingRulesResponse>> Put(Guid id, SetRoutingRulesRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion == Guid.Empty || InboxRouting.ShapeError(request.Rules) is { })
            return BadRequest(new ProblemDetails { Status = 400, Title = InboxRouting.ShapeError(request.Rules) ?? "ExpectedVersion cannot be empty" });
        foreach (var rule in request.Rules!)
        {
            try { SegmentCompiler.Validate(rule.Conditions); }
            catch (SegmentRuleException ex) { return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Invalid routing condition", Detail = ex.Message }); }
        }
        var canonical = JsonSerializer.Serialize(request.Rules.Select(r => r with { Name = r.Name.Trim() }).ToList(), InboxRouting.Json);
        if (canonical.Length > 100_000) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Routing definition exceeds 100,000 characters" });
        return await WriteTransactions.RunAsync<ActionResult<RoutingRulesResponse>>(db, async () =>
        {
            if (!await Parent(id, ct)) return NotFound();
            var workspaceId = User.GetWorkspaceId();
            var teamIds = request.Rules.Select(r => r.TargetTeamId).Distinct().ToArray();
            if (await db.Teams.CountAsync(t => t.WorkspaceId == workspaceId && teamIds.Contains(t.Id), ct) != teamIds.Length)
                return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable routing team" });
            var definition = await db.InboxRoutingDefinitions.SingleOrDefaultAsync(d => d.InboxId == id && d.WorkspaceId == workspaceId, ct);
            var create = definition is null;
            if ((create && request.ExpectedVersion is not null) || (!create && request.ExpectedVersion != definition!.Version))
                return Conflict(new ProblemDetails { Status = 409, Title = "Routing configuration changed" });
            var version = new InboxRoutingVersion { WorkspaceId = workspaceId, InboxId = id,
                CreatedByTeammateId = User.GetTeammateId(), CreatedAt = clock.GetUtcNow(), RulesJson = canonical };
            db.InboxRoutingVersions.Add(version);
            if (create)
            {
                definition = new InboxRoutingDefinition { WorkspaceId = workspaceId, InboxId = id, ActiveVersionId = version.Id };
                db.InboxRoutingDefinitions.Add(definition);
            }
            else definition!.ActiveVersionId = version.Id;
            await db.SaveChangesAsync(ct);
            var response = new RoutingRulesResponse(id, definition!.Version, version.Id,
                JsonSerializer.Deserialize<List<RoutingRule>>(canonical, InboxRouting.Json)!);
            return create ? CreatedAtAction(nameof(Get), new { id }, response) : response;
        }, ct);
    }

    [HttpPost("api/inboxes/{id:guid}/routing-rules/preview")]
    public async Task<ActionResult<RoutingDecision>> Preview(Guid id, PreviewRoutingRequest request, CancellationToken ct)
    {
        if (!await Parent(id, ct)) return NotFound();
        var workspaceId = User.GetWorkspaceId();
        if (!await db.Contacts.AnyAsync(c => c.Id == request.ContactId && c.WorkspaceId == workspaceId, ct))
            return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable contact" });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var decision = await InboxRouting.EvaluateAsync(db, workspaceId, id, request.ContactId!.Value, request.Priority, request.Channel, ct);
        await transaction.CommitAsync(ct);
        return decision;
    }
}
