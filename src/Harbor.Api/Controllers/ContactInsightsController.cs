using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ContactInsightsController(HarborDbContext db) : ControllerBase
{
    private IQueryable<Contact> Contacts() => db.Contacts.Where(c => c.WorkspaceId == User.GetWorkspaceId());
    private static ContactAttributesResponse Attributes(Contact contact) => new(ContactAttributes.Read(contact.AttributesJson), contact.Version);

    [HttpGet("api/contacts/{id:guid}/attributes")]
    public async Task<ActionResult<ContactAttributesResponse>> GetAttributes(Guid id, CancellationToken ct)
    {
        var contact = await Contacts().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return contact is null ? NotFound() : Attributes(contact);
    }

    [HttpPatch("api/contacts/{id:guid}/attributes")]
    public async Task<ActionResult<ContactAttributesResponse>> PatchAttributes(Guid id, PatchAttributesRequest request, CancellationToken ct)
    {
        var contact = await Contacts().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null) return NotFound();
        if (contact.Version != request.ExpectedVersion)
            return Conflict(new ProblemDetails { Status = 409, Title = "Stale contact", Detail = "Reload the attributes and version before retrying your patch." });
        var result = ContactAttributePatch.Apply(contact.AttributesJson, request.Changes);
        if (result.Error is { } error) return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid attribute changes", Detail = error });
        contact.AttributesJson = result.Json!;
        await db.SaveChangesAsync(ct);
        return Attributes(contact);
    }

    private IQueryable<Contact> WithEmail(Guid workspaceId) => Contacts().AsNoTracking()
        .Where(c => c.WorkspaceId == workspaceId && c.Email != null
            && HarborDbContext.TrimCharacters(c.Email, QueryText.WhitespaceCharacters) != "");

    [HttpGet("api/workspaces/{workspaceId:guid}/contacts/duplicate-emails")]
    public async Task<ActionResult<List<DuplicateEmailResponse>>> DuplicateEmails(Guid workspaceId, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        return await WithEmail(workspaceId)
            .GroupBy(c => HarborDbContext.TrimCharacters(c.Email, QueryText.WhitespaceCharacters)!.ToLower())
            .Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => new DuplicateEmailResponse(g.Key, g.Count())).ToPageAsync(paging, Response, ct);
    }

    [HttpGet("api/workspaces/{workspaceId:guid}/contacts/duplicate-emails/members")]
    public async Task<ActionResult<List<ContactResponse>>> DuplicateMembers(Guid workspaceId,
        [FromQuery, Required, MaxLength(320)] string email, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = WithEmail(workspaceId).Where(c =>
            HarborDbContext.TrimCharacters(c.Email, QueryText.WhitespaceCharacters)!.ToLower() == normalized);
        if (await query.CountAsync(ct) < 2) query = query.Where(c => false);
        var result = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => c.ToResponse()).ToPageAsync(paging, Response, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    [HttpGet("api/contacts/{id:guid}/support-summary")]
    public async Task<ActionResult<ContactSupportResponse>> SupportSummary(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await Contacts().AnyAsync(c => c.Id == id, ct)) return NotFound();
        var query = db.Conversations.AsNoTracking().Where(c => c.WorkspaceId == User.GetWorkspaceId() && c.ContactId == id);
        var counts = await query.GroupBy(c => 1).Select(g => new
        {
            Total = g.Count(), Open = g.Count(c => c.State == ConversationState.Open),
            Snoozed = g.Count(c => c.State == ConversationState.Snoozed), Closed = g.Count(c => c.State == ConversationState.Closed),
            First = g.Min(c => c.CreatedAt),
        }).SingleOrDefaultAsync(ct);
        var ids = query.Select(c => c.Id);
        var messages = await db.Messages.Where(m => ids.Contains(m.ConversationId)).GroupBy(m => 1)
            .Select(g => new { Replies = g.Count(m => m.Kind == MessageKind.Reply), Notes = g.Count(m => m.Kind == MessageKind.Note) })
            .SingleOrDefaultAsync(ct);
        var latest = await query.OrderByDescending(c => c.LastMessageAt).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.LastMessageAt }).FirstOrDefaultAsync(ct);
        await transaction.CommitAsync(ct);
        return new ContactSupportResponse(counts?.Total ?? 0, counts?.Open ?? 0, counts?.Snoozed ?? 0, counts?.Closed ?? 0,
            messages?.Replies ?? 0, messages?.Notes ?? 0, counts?.First, latest?.LastMessageAt, latest?.Id);
    }
}
