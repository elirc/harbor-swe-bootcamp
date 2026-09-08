using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ApiKeysController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private bool CanManage(Guid teammateId) => User.GetTeammateId() == teammateId || User.IsAdmin();

    [HttpGet("api/teammates/{id:guid}/api-keys")]
    public async Task<IActionResult> List(Guid id, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        if (!await db.Teammates.AnyAsync(t => t.Id == id && t.WorkspaceId == User.GetWorkspaceId(), ct)) return NotFound();
        if (!CanManage(id)) return Forbid();
        Response.Headers.CacheControl = "no-store";
        return Ok(await db.TeammateApiKeys.AsNoTracking().Where(k => k.TeammateId == id && k.WorkspaceId == User.GetWorkspaceId())
            .OrderByDescending(k => k.CreatedAt).ThenBy(k => k.Id)
            .Select(k => new ApiKeyMetadata(k.Id, k.TeammateId, k.Name, k.CreatedAt, k.ExpiresAt, k.RevokedAt))
            .ToPageAsync(paging, Response, ct));
    }

    [HttpPost("api/teammates/{id:guid}/api-keys")]
    public Task<ActionResult<CreatedApiKeyResponse>> Create(Guid id, CreateApiKeyRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<CreatedApiKeyResponse>>(db, async () =>
        {
            var workspaceId = User.GetWorkspaceId();
            if (!await db.Teammates.AnyAsync(t => t.Id == id && t.WorkspaceId == workspaceId, ct)) return NotFound();
            if (!CanManage(id)) return Forbid();
            var now = clock.GetUtcNow();
            var count = await db.TeammateApiKeys.CountAsync(k => k.TeammateId == id && k.WorkspaceId == workspaceId
                && k.RevokedAt == null && (k.ExpiresAt == null || k.ExpiresAt > now), ct);
            if (count >= 5) return Conflict(new ProblemDetails { Status = 409, Title = "Five usable keys already exist", Detail = "Revoke an unused key before creating another." });
            var raw = ApiKeys.Generate();
            var key = new TeammateApiKey { WorkspaceId = workspaceId, TeammateId = id, Name = request.Name.Trim(),
                Hash = ApiKeys.Hash(raw), CreatedAt = now, ExpiresAt = now.AddMinutes(request.LifetimeMinutes!.Value) };
            db.TeammateApiKeys.Add(key);
            await db.SaveChangesAsync(ct);
            Response.Headers.CacheControl = "no-store";
            return CreatedAtAction(nameof(List), new { id }, new CreatedApiKeyResponse(
                new ApiKeyMetadata(key.Id, id, key.Name, key.CreatedAt, key.ExpiresAt, key.RevokedAt), raw));
        }, ct);

    [HttpDelete("api/api-keys/{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var key = await db.TeammateApiKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == id && k.WorkspaceId == User.GetWorkspaceId(), ct);
        if (key is null) return NotFound();
        if (!CanManage(key.TeammateId)) return Forbid();
        var now = clock.GetUtcNow();
        await db.TeammateApiKeys.Where(k => k.Id == id && k.WorkspaceId == User.GetWorkspaceId() && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, now), ct);
        return NoContent();
    }
}
