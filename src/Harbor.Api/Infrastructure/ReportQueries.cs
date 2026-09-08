using Harbor.Api.Contracts;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Infrastructure;

public static class ReportQueries
{
    public static IQueryable<Conversation> Build(HarborDbContext db, Guid workspaceId, ReportFilterRequest filter)
    {
        var query = db.Conversations.AsNoTracking().Where(c => c.WorkspaceId == workspaceId);
        if (filter.From is { } from) query = query.Where(c => c.CreatedAt >= from);
        if (filter.To is { } to) query = query.Where(c => c.CreatedAt < to);
        if (filter.InboxId is { } inbox) query = query.Where(c => c.InboxId == inbox);
        if (filter.AssignedTeammateId is { } teammate) query = query.Where(c => c.AssignedTeammateId == teammate);
        if (filter.AssignedTeamId is { } team) query = query.Where(c => c.AssignedTeamId == team);
        if (filter.Priority is { } priority) query = query.Where(c => c.Priority == priority);
        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var name = filter.Tag.Trim().ToLower();
            query = query.Where(c => c.Tags.Any(t => t.Tag!.Name.ToLower() == name));
        }
        return query;
    }
}
