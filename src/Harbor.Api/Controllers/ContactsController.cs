using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ContactsController(HarborDbContext db) : ControllerBase
{
    [HttpPost("api/workspaces/{workspaceId:guid}/contacts")]
    public async Task<ActionResult<ContactResponse>> Create(Guid workspaceId, CreateContactRequest request)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId))
        {
            return NotFound();
        }

        var contact = new Contact
        {
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Email = request.Email,
            ExternalId = request.ExternalId,
            AttributesJson = ContactAttributes.Write(request.Attributes),
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = contact.Id }, contact.ToResponse());
    }

    /// <summary>Lists contacts with scoped search and optional presence/history filters.</summary>
    [HttpGet("api/workspaces/{workspaceId:guid}/contacts")]
    public async Task<ActionResult<List<ContactResponse>>> List(
        Guid workspaceId, [FromQuery] ContactFilterRequest filter, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
        {
            return NotFound();
        }

        return await ContactQueries.Build(db, workspaceId, filter)
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => c.ToResponse())
            .ToPageAsync(filter, Response, cancellationToken);
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/contacts/overview")]
    public async Task<ActionResult<ContactOverviewResponse>> Overview(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken)) return NotFound();
        var counts = await db.Contacts.AsNoTracking().Where(c => c.WorkspaceId == workspaceId)
            .GroupBy(c => 1)
            .Select(g => new ContactOverviewResponse(g.Count(),
                g.Count(c => c.Email != null && HarborDbContext.TrimCharacters(c.Email, QueryText.WhitespaceCharacters) != ""),
                g.Count(c => c.Email == null || HarborDbContext.TrimCharacters(c.Email, QueryText.WhitespaceCharacters) == "")))
            .SingleOrDefaultAsync(cancellationToken);
        return counts ?? new ContactOverviewResponse(0, 0, 0);
    }

    [HttpGet("api/contacts/{id:guid}")]
    public async Task<ActionResult<ContactResponse>> GetById(Guid id)
    {
        var contact = await db.Contacts
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        return contact is null ? NotFound() : contact.ToResponse();
    }

    [HttpPut("api/contacts/{id:guid}")]
    public async Task<ActionResult<ContactResponse>> Update(Guid id, UpdateContactRequest request)
    {
        var contact = await db.Contacts
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (contact is null)
        {
            return NotFound();
        }

        contact.Name = request.Name.Trim();
        contact.Email = request.Email;
        contact.ExternalId = request.ExternalId;
        contact.AttributesJson = ContactAttributes.Write(request.Attributes);
        await db.SaveChangesAsync();

        return contact.ToResponse();
    }

    [HttpDelete("api/contacts/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var contact = await db.Contacts
            .SingleOrDefaultAsync(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId());
        if (contact is null)
        {
            return NotFound();
        }

        var hasConversations = await db.Conversations.AnyAsync(c => c.ContactId == id);
        if (hasConversations)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Contact has conversations",
                Detail = "A contact with existing conversations cannot be deleted.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        db.Contacts.Remove(contact);
        await db.SaveChangesAsync();

        return NoContent();
    }
}
