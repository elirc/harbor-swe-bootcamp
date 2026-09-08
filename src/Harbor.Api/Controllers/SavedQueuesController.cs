using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class SavedQueuesController(HarborDbContext db, ConversationQueries queries) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    private IQueryable<SavedQueue> Own() => db.SavedQueues.Where(q =>
        q.WorkspaceId == User.GetWorkspaceId() && q.OwnerTeammateId == User.GetTeammateId());

    private static SavedQueueFilter? ReadFilter(SavedQueue row)
    {
        if (row.SchemaVersion != 1 || row.FilterJson.Length > 10_000) return null;
        try
        {
            var filter = JsonSerializer.Deserialize<SavedQueueFilter>(row.FilterJson, Json);
            return filter is not null && Validator.TryValidateObject(filter, new ValidationContext(filter), [], true)
                ? filter : null;
        }
        catch (JsonException) { return null; }
    }

    private static SavedQueueResponse Map(SavedQueue row, SavedQueueFilter filter) =>
        new(row.Id, row.Name, filter, row.SchemaVersion, row.CreatedAt, row.UpdatedAt);

    private ObjectResult InvalidRecipe() => UnprocessableEntity(new ProblemDetails
    { Status = 422, Title = "Unavailable saved filters", Detail = "The filter schema or stored configuration is unsupported or invalid. Update this queue before executing it." });

    [HttpPost("api/workspaces/{workspaceId:guid}/saved-queues")]
    public async Task<ActionResult<SavedQueueResponse>> Create(Guid workspaceId, SaveQueueRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request.Filters, Json);
        if (json.Length > 10_000) return BadRequest(new ProblemDetails { Status = 400, Title = "Filters exceed 10000 characters" });
        var row = new SavedQueue { WorkspaceId = workspaceId, OwnerTeammateId = User.GetTeammateId(), Name = request.Name.Trim(), FilterJson = json };
        db.SavedQueues.Add(row);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = row.Id }, Map(row, request.Filters));
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/saved-queues")]
    public async Task<ActionResult<List<SavedQueueResponse>>> List(Guid workspaceId, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        var rows = await Own().AsNoTracking().Where(q => q.WorkspaceId == workspaceId)
            .OrderBy(q => q.Name).ThenBy(q => q.Id).ToPageAsync(paging, Response, ct);
        var results = new List<SavedQueueResponse>();
        foreach (var row in rows)
        {
            var filter = ReadFilter(row);
            if (filter is null) return InvalidRecipe();
            results.Add(Map(row, filter));
        }
        return results;
    }

    [HttpGet("api/saved-queues/{id:guid}")]
    public async Task<ActionResult<SavedQueueResponse>> Get(Guid id, CancellationToken ct)
    {
        var row = await Own().AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, ct);
        if (row is null) return NotFound();
        var filter = ReadFilter(row);
        return filter is null ? InvalidRecipe() : Map(row, filter);
    }

    [HttpPut("api/saved-queues/{id:guid}")]
    public async Task<ActionResult<SavedQueueResponse>> Update(Guid id, SaveQueueRequest request, CancellationToken ct)
    {
        var row = await Own().SingleOrDefaultAsync(q => q.Id == id, ct);
        if (row is null) return NotFound();
        var json = JsonSerializer.Serialize(request.Filters, Json);
        if (json.Length > 10_000) return BadRequest(new ProblemDetails { Status = 400, Title = "Filters exceed 10000 characters" });
        row.Name = request.Name.Trim();
        row.FilterJson = json;
        row.SchemaVersion = 1;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(row, request.Filters);
    }

    [HttpDelete("api/saved-queues/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await Own().SingleOrDefaultAsync(q => q.Id == id, ct);
        if (row is null) return NotFound();
        db.SavedQueues.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("api/saved-queues/{id:guid}/conversations")]
    public async Task<ActionResult<List<ConversationSummaryResponse>>> Execute(Guid id, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        var row = await Own().AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, ct);
        if (row is null) return NotFound();
        var recipe = ReadFilter(row);
        if (recipe is null) return InvalidRecipe();
        var filter = recipe.ToRuntime(paging.Page, paging.PageSize);
        var query = await queries.BuildAsync(row.WorkspaceId, filter, DateTimeOffset.UtcNow, ct);
        if (query is null) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unknown saved segment", Detail = "Update the saved queue to select an available segment." });
        var conversations = await ConversationQueries.Order(query, filter.Sort)
            .Include(c => c.Tags).ThenInclude(t => t.Tag).ToPageAsync(filter, Response, ct);
        return conversations.Select(c => c.ToSummaryResponse()).ToList();
    }
}
