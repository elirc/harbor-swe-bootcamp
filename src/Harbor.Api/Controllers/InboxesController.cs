using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId:guid}/inboxes")]
public class InboxesController(HarborDbContext db) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<InboxResponse>> Create(Guid workspaceId, CreateInboxRequest request)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        // Addresses are stored lowercase so inbound routing can match exactly.
        var emailAddress = string.IsNullOrWhiteSpace(request.EmailAddress)
            ? null
            : request.EmailAddress.Trim().ToLowerInvariant();

        if (emailAddress is not null
            && await db.Inboxes.AnyAsync(i =>
                i.WorkspaceId == workspaceId && i.EmailAddress == emailAddress))
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate inbox address",
                Detail = $"Another inbox in this workspace already receives mail at '{emailAddress}'.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        var inbox = new Inbox
        {
            WorkspaceId = workspaceId,
            Name = request.Name,
            FirstResponseSlaMinutes = request.FirstResponseSlaMinutes,
            AutoAssign = request.AutoAssign,
            EmailAddress = emailAddress,
        };
        db.Inboxes.Add(inbox);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { workspaceId, id = inbox.Id }, inbox.ToResponse());
    }

    [HttpGet]
    public async Task<ActionResult<List<InboxResponse>>> List(
        Guid workspaceId, [FromQuery] InboxFilterRequest filter, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var query = db.Inboxes.AsNoTracking().Where(i => i.WorkspaceId == workspaceId);
        if (!filter.IncludeArchived) query = query.Where(i => i.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var needle = filter.Q.Trim().ToLowerInvariant();
            query = query.Where(i => i.Name.ToLower().Contains(needle)
                || (i.EmailAddress != null && i.EmailAddress.ToLower().Contains(needle)));
        }
        if (filter.AutoAssign is { } autoAssign)
        {
            query = query.Where(i => i.AutoAssign == autoAssign);
        }

        return await query
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => i.ToResponse())
            .ToPageAsync(filter, Response, cancellationToken);
    }

    [HttpPut("{id:guid}/name")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<InboxResponse>> Rename(
        Guid workspaceId, Guid id, UpdateInboxNameRequest request, CancellationToken cancellationToken)
    {
        var inbox = await db.Inboxes.SingleOrDefaultAsync(
            i => i.Id == id && i.WorkspaceId == workspaceId, cancellationToken);
        if (inbox is null) return NotFound();

        inbox.Name = request.Name.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return inbox.ToResponse();
    }

    [HttpPut("{id:guid}/auto-assign")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<InboxResponse>> SetAutoAssign(
        Guid workspaceId, Guid id, UpdateInboxAutoAssignRequest request, CancellationToken cancellationToken)
    {
        var inbox = await db.Inboxes.SingleOrDefaultAsync(
            i => i.Id == id && i.WorkspaceId == workspaceId, cancellationToken);
        if (inbox is null) return NotFound();

        inbox.AutoAssign = request.AutoAssign!.Value;
        await db.SaveChangesAsync(cancellationToken);
        return inbox.ToResponse();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InboxResponse>> GetById(Guid workspaceId, Guid id)
    {
        var inbox = await db.Inboxes
            .SingleOrDefaultAsync(i => i.Id == id && i.WorkspaceId == workspaceId);

        return inbox is null ? NotFound() : inbox.ToResponse();
    }
}
