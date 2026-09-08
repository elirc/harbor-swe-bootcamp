using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record CreateWorkingCalendarRequest([Required, MaxLength(200)] string Name, [Required] WorkingCalendarDefinition Definition);
public sealed record WorkingCalendarResponse(Guid Id, Guid WorkspaceId, string Name, WorkingCalendarDefinition Definition, DateTimeOffset CreatedAt);

[ApiController, Authorize(Roles = "Admin"), Route("api/workspaces/{workspaceId:guid}/working-calendars")]
public sealed class WorkingCalendarsController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkingCalendarResponse>> Create(Guid workspaceId, CreateWorkingCalendarRequest request, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest();
        var definition = WorkingTime.Normalize(request.Definition);
        var calendar = new WorkingCalendar { WorkspaceId = workspaceId, Name = name, DefinitionJson = JsonSerializer.Serialize(definition),
            CreatedAt = clock.GetUtcNow(), CreatedByTeammateId = User.GetTeammateId() };
        db.WorkingCalendars.Add(calendar);
        await db.SaveChangesAsync(ct);
        return Created($"/api/workspaces/{workspaceId}/working-calendars", new WorkingCalendarResponse(calendar.Id, workspaceId, name, definition, calendar.CreatedAt));
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkingCalendarResponse>>> List(Guid workspaceId, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        var rows = await db.WorkingCalendars.AsNoTracking().Where(c => c.WorkspaceId == workspaceId)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).ToPageAsync(paging, Response, ct);
        return rows.Select(c => new WorkingCalendarResponse(c.Id, c.WorkspaceId, c.Name,
            JsonSerializer.Deserialize<WorkingCalendarDefinition>(c.DefinitionJson)!, c.CreatedAt)).ToList();
    }
}
