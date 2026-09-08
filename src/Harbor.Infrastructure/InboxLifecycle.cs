using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

public static class InboxLifecycle
{
    public static void RequireActiveForNewWork(Inbox inbox)
    {
        if (inbox.ArchivedAt is not null) throw new DomainException("This inbox is archived and cannot receive new conversations or transfers.");
    }
}
