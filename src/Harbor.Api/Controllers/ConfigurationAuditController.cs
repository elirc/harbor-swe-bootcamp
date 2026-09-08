using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record ConfigurationAuditFilter : PageRequest, IValidatableObject
{
    public Guid? ActorId { get; init; }
    public Guid? EntityId { get; init; }
    [EnumDataType(typeof(ConfigurationAuditAction))] public ConfigurationAuditAction? Action { get; init; }
    [EnumDataType(typeof(ConfigurationEntityKind))] public ConfigurationEntityKind? EntityKind { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? Before { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    { if (From is { } from && Before is { } before && from >= before) yield return new("From must precede Before.", [nameof(From), nameof(Before)]); }
}
public sealed record ConfigurationAuditResponse(Guid Id, Guid ActorTeammateId, Guid EntityId, Guid? RelatedTeammateId,
    ConfigurationEntityKind EntityKind, ConfigurationAuditAction Action, IReadOnlyList<string> ChangedFields, JsonElement? AllowedChanges, DateTimeOffset OccurredAt);

[ApiController, Authorize(Roles = "Admin")]
public sealed class ConfigurationAuditController(HarborDbContext db) : ControllerBase
{
    [HttpGet("api/workspaces/{workspaceId:guid}/configuration-audit")]
    public async Task<ActionResult<List<ConfigurationAuditResponse>>> List(Guid workspaceId, [FromQuery] ConfigurationAuditFilter filter, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        var query = db.ConfigurationAuditEvents.AsNoTracking().Where(e => e.WorkspaceId == workspaceId);
        if (filter.ActorId is { } actor) query = query.Where(e => e.ActorTeammateId == actor);
        if (filter.EntityId is { } id) query = query.Where(e => e.EntityId == id);
        if (filter.EntityKind is { } kind) query = query.Where(e => e.EntityKind == kind);
        if (filter.Action is { } action) query = query.Where(e => e.Action == action);
        if (filter.From is { } from) query = query.Where(e => e.OccurredAt >= from);
        if (filter.Before is { } before) query = query.Where(e => e.OccurredAt < before);
        var rows = await query.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id).ToPageAsync(filter, Response, ct);
        return rows.Select(e => new ConfigurationAuditResponse(e.Id, e.ActorTeammateId, e.EntityId, e.RelatedTeammateId, e.EntityKind, e.Action,
            JsonSerializer.Deserialize<string[]>(e.ChangedFieldsJson)!, e.AllowedChangesJson is null ? null : JsonSerializer.Deserialize<JsonElement>(e.AllowedChangesJson), e.OccurredAt)).ToList();
    }
}
