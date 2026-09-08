using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

public static class WebhookQueries
{
    public static IQueryable<WebhookDelivery> Scoped(HarborDbContext db, Guid workspaceId) =>
        db.WebhookDeliveries.Where(d => d.WorkspaceId == workspaceId && d.Subscription != null
            && d.Subscription.WorkspaceId == workspaceId);

    public static IQueryable<WebhookDelivery> Ready(HarborDbContext db, Guid workspaceId, DateTimeOffset now) =>
        Scoped(db, workspaceId).Where(d => d.Status == WebhookDeliveryStatus.Pending
            && d.Subscription!.IsActive && d.NextAttemptAt <= now);
}
