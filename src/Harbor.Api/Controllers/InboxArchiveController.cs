using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record SetInboxArchivedRequest([Required] bool? Archived);

[ApiController, Authorize(Roles = "Admin")]
public sealed class InboxArchiveController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpPut("api/inboxes/{id:guid}/archive")]
    public Task<ActionResult<InboxResponse>> Set(Guid id, SetInboxArchivedRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<InboxResponse>>(db, async () =>
        {
            var inbox = await db.Inboxes.SingleOrDefaultAsync(i => i.Id == id && i.WorkspaceId == User.GetWorkspaceId(), ct);
            if (inbox is null) return NotFound();
            if (request.Archived!.Value != (inbox.ArchivedAt is not null))
            {
                inbox.ArchivedAt = request.Archived.Value ? clock.GetUtcNow() : null;
                await db.SaveChangesAsync(ct);
            }
            return inbox.ToResponse();
        }, ct);
}
