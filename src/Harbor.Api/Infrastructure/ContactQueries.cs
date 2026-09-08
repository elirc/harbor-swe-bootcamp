using Harbor.Api.Contracts;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Infrastructure;

/// <summary>Builds contact membership in SQL before ordering, counting, or paging.</summary>
public static class ContactQueries
{
    public static IQueryable<Contact> Build(
        HarborDbContext db, Guid workspaceId, ContactFilterRequest filter)
    {
        var query = db.Contacts.AsNoTracking().Where(c => c.WorkspaceId == workspaceId);
        var whitespace = QueryText.WhitespaceCharacters;

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var needle = filter.Q.Trim().ToLowerInvariant();
            query = query.Where(c => c.Name.ToLower().Contains(needle)
                || (c.Email != null && c.Email.ToLower().Contains(needle))
                || (c.ExternalId != null && c.ExternalId.ToLower().Contains(needle)));
        }

        if (filter.HasEmail is { } hasEmail)
        {
            query = hasEmail
                ? query.Where(c => c.Email != null && HarborDbContext.TrimCharacters(c.Email, whitespace) != "")
                : query.Where(c => c.Email == null || HarborDbContext.TrimCharacters(c.Email, whitespace) == "");
        }

        if (filter.HasExternalId is { } hasExternalId)
        {
            query = hasExternalId
                ? query.Where(c => c.ExternalId != null && HarborDbContext.TrimCharacters(c.ExternalId, whitespace) != "")
                : query.Where(c => c.ExternalId == null || HarborDbContext.TrimCharacters(c.ExternalId, whitespace) == "");
        }

        if (filter.HasConversations is { } hasConversations)
        {
            query = query.Where(c => db.Conversations.Any(conversation =>
                conversation.WorkspaceId == workspaceId && conversation.ContactId == c.Id) == hasConversations);
        }

        return query;
    }
}
