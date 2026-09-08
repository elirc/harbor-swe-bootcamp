using System.Text.Json;
using Harbor.Api.Infrastructure;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public sealed class WorkspaceEventsController(DbContextOptions<HarborDbContext> options, EventCursors cursors, TimeProvider clock) : ControllerBase
{
    [HttpGet("api/workspaces/{workspaceId:guid}/events")]
    public async Task<IActionResult> Stream(Guid workspaceId, CancellationToken ct)
    {
        long after;
        await using (var db = new HarborDbContext(options))
        {
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
            var watermark = await db.WorkspaceEventWatermarks.Where(w => w.WorkspaceId == workspaceId).Select(w => (long?)w.PurgedThrough).SingleOrDefaultAsync(ct) ?? 0;
            if (Request.Headers.TryGetValue("Last-Event-ID", out var header))
            {
                if (header.Count != 1) return BadRequest();
                var parsed = cursors.Read(workspaceId, header[0] ?? "");
                if (parsed.Status != 200) return CursorError(parsed.Status);
                after = parsed.Cursor!.Sequence;
                if (after < watermark) return CursorError(410);
            }
            else after = await db.WorkspaceEvents.Where(e => e.WorkspaceId == workspaceId).Select(e => (long?)e.Sequence).MaxAsync(ct) ?? watermark;
        }
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        var token = lifetime.Token;
        try
        {
            await Frame("ready", cursors.Issue(workspaceId, after), new { refresh = false }, token);
            while (!token.IsCancellationRequested)
            {
                List<Harbor.Domain.Entities.WorkspaceEvent> rows;
                await using (var db = new HarborDbContext(options))
                {
                    var watermark = await db.WorkspaceEventWatermarks.Where(w => w.WorkspaceId == workspaceId).Select(w => (long?)w.PurgedThrough).SingleOrDefaultAsync(token) ?? 0;
                    if (after < watermark)
                    { await Frame("refresh-required", null, new { reason = "History was pruned; refresh ordinary APIs and reconnect without a cursor." }, token); break; }
                    rows = await db.WorkspaceEvents.AsNoTracking().Where(e => e.WorkspaceId == workspaceId && e.Sequence > after)
                        .OrderBy(e => e.Sequence).Take(100).ToListAsync(token);
                }
                foreach (var row in rows)
                {
                    await Frame(row.Kind.ToString(), cursors.Issue(workspaceId, row.Sequence), new
                    { eventId = row.EventId, sequence = row.Sequence, version = row.SchemaVersion, entityId = row.EntityId,
                        conversationId = row.ConversationId, recordedAt = row.RecordedAt, metadata = JsonSerializer.Deserialize<JsonElement>(row.MetadataJson) }, token);
                    after = row.Sequence;
                }
                if (rows.Count == 100) continue;
                await Response.WriteAsync(": heartbeat\n\n", token);
                await Response.Body.FlushAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        return new EmptyResult();
    }

    [HttpPost("api/workspaces/{workspaceId:guid}/events/prune"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Prune(Guid workspaceId, CancellationToken ct)
    {
        await using var db = new HarborDbContext(options);
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        return Ok(new { removed = await WorkspaceEvents.PruneAsync(db, workspaceId, clock.GetUtcNow(), ct) });
    }
    private ObjectResult CursorError(int status) => StatusCode(status, new ProblemDetails
    { Status = status, Title = status == 410 ? "Refresh ordinary APIs and reconnect without a cursor." : "Invalid workspace event cursor." });
    private async Task Frame(string kind, string? id, object data, CancellationToken ct)
    {
        if (id is not null) await Response.WriteAsync($"id: {id}\n", ct);
        await Response.WriteAsync($"event: {kind}\ndata: {JsonSerializer.Serialize(data)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
