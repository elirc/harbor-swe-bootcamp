using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

/// <summary>Commit ownership and attempt evidence before HTTP; complete only while the lease remains owned.</summary>
public class WebhookDispatcher(DbContextOptions<HarborDbContext> options, IWebhookSender sender, TimeProvider clock)
{
    public sealed record Claim(WebhookDelivery Delivery, Guid Token, long Generation, bool ShouldSend);

    public async Task<List<WebhookDelivery>> DispatchAsync(Guid workspaceId, DateTimeOffset now,
        CancellationToken cancellationToken = default, int limit = 50)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var attempted = new List<WebhookDelivery>();
        // S03 caps actual claims at 50, including requests from older M28 clients with larger limits.
        for (var i = 0; i < Math.Min(limit, 50); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await ClaimNextAsync(workspaceId, clock.GetUtcNow(), cancellationToken);
            if (claim is null) break;
            if (!claim.ShouldSend) continue;
            if (cancellationToken.IsCancellationRequested)
            {
                await AbandonBeforeSendAsync(claim, "CancelledBeforeTransport", clock.GetUtcNow());
                cancellationToken.ThrowIfCancellationRequested();
            }
            try
            {
                await using var check = new HarborDbContext(options);
                var beforeSend = clock.GetUtcNow();
                if (!await check.WebhookDeliveries.AnyAsync(d => d.Id == claim.Delivery.Id && d.LeaseToken == claim.Token
                    && d.ClaimGeneration == claim.Generation && d.LeaseExpiresAt > beforeSend, cancellationToken)) continue;
                if (!await check.WebhookSubscriptions.AnyAsync(s => s.Id == claim.Delivery.SubscriptionId
                    && s.WorkspaceId == workspaceId && s.IsActive, cancellationToken))
                {
                    await AbandonBeforeSendAsync(claim, "SubscriptionPaused", clock.GetUtcNow());
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await AbandonBeforeSendAsync(claim, "CancelledBeforeTransport", clock.GetUtcNow());
                throw;
            }
            WebhookSendResult result;
            try { result = await sender.SendAsync(claim.Delivery.Subscription!, claim.Delivery, clock.GetUtcNow(), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            { result = WebhookSendResult.Unreachable("TransportError"); }
            // A cancelled in-flight attempt remains Started until expiry exposes it as Unknown.
            cancellationToken.ThrowIfCancellationRequested();
            await CompleteAsync(claim, result, clock.GetUtcNow(), cancellationToken);
            await using var read = new HarborDbContext(options);
            var current = await WebhookQueries.Scoped(read, workspaceId).AsNoTracking()
                .SingleOrDefaultAsync(d => d.Id == claim.Delivery.Id, cancellationToken);
            if (current is not null) attempted.Add(current);
        }
        return attempted;
    }

    public async Task<Claim?> ClaimNextAsync(Guid workspaceId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = new HarborDbContext(options);
        return await WriteTransactions.RunAsync<Claim?>(db, async () =>
        {
            var delivery = await WebhookQueries.Ready(db, workspaceId, now)
                .Where(d => d.LeaseToken == null || d.LeaseExpiresAt == null || d.LeaseExpiresAt <= now)
                .Include(d => d.Subscription).OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).FirstOrDefaultAsync(ct);
            if (delivery is null) return null;
            if (delivery.LeaseToken is not null)
            {
                var abandoned = await db.WebhookAttempts.SingleOrDefaultAsync(a => a.DeliveryId == delivery.Id
                    && a.ClaimGeneration == delivery.ClaimGeneration && a.Outcome == WebhookAttemptOutcome.Started, ct);
                if (abandoned is not null)
                { abandoned.Outcome = WebhookAttemptOutcome.Unknown; abandoned.FinishedAt = now; abandoned.FailureCode = "LeaseExpired"; }
            }
            delivery.LeaseToken = null;
            delivery.LeaseExpiresAt = null;
            if (delivery.AttemptCount >= WebhookDelivery.MaxAttempts)
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                delivery.Error = "RetryBudgetExhausted";
                await db.SaveChangesAsync(ct);
                return new Claim(delivery, Guid.Empty, delivery.ClaimGeneration, false);
            }
            delivery.AttemptCount++;
            delivery.LastAttemptAt = now;
            delivery.ClaimGeneration++;
            delivery.LeaseToken = Guid.NewGuid();
            delivery.LeaseExpiresAt = now.AddSeconds(60);
            db.WebhookAttempts.Add(new WebhookAttempt { WorkspaceId = workspaceId, DeliveryId = delivery.Id,
                ClaimGeneration = delivery.ClaimGeneration, AttemptNumber = delivery.AttemptCount, StartedAt = now });
            await db.SaveChangesAsync(ct);
            return new Claim(delivery, delivery.LeaseToken.Value, delivery.ClaimGeneration, true);
        }, ct);
    }

    public Task<bool> CompleteAsync(Claim claim, WebhookSendResult result, DateTimeOffset now, CancellationToken ct = default)
        => Finish(claim, result, null, now, ct);

    private Task<bool> AbandonBeforeSendAsync(Claim claim, string reason, DateTimeOffset now) =>
        Finish(claim, WebhookSendResult.Unreachable(reason), reason, now, CancellationToken.None);

    private async Task<bool> Finish(Claim claim, WebhookSendResult result, string? unknownReason, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = new HarborDbContext(options);
        return await WriteTransactions.RunAsync(db, async () =>
        {
            var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(d => d.Id == claim.Delivery.Id
                && d.WorkspaceId == claim.Delivery.WorkspaceId && d.LeaseToken == claim.Token
                && d.ClaimGeneration == claim.Generation && d.LeaseExpiresAt > now && d.Status == WebhookDeliveryStatus.Pending, ct);
            if (delivery is null) return false;
            var attempt = await db.WebhookAttempts.SingleAsync(a => a.DeliveryId == delivery.Id && a.ClaimGeneration == claim.Generation, ct);
            attempt.FinishedAt = now;
            attempt.ResponseStatusCode = result.StatusCode;
            attempt.Outcome = unknownReason is not null ? WebhookAttemptOutcome.Unknown
                : result.Success ? WebhookAttemptOutcome.Succeeded
                : result.StatusCode is not null ? WebhookAttemptOutcome.Rejected : WebhookAttemptOutcome.TransportError;
            attempt.FailureCode = unknownReason ?? (result.Success ? null : result.StatusCode is not null ? "HttpRejected" : "TransportError");
            if (result.Success) delivery.Succeed(result.StatusCode!.Value, now, attemptReserved: true);
            else
            {
                // Preserve the existing administrator delivery diagnostic; the ledger exposes only a safe code.
                var diagnostic = result.Error ?? attempt.FailureCode!;
                delivery.Fail(result.StatusCode, diagnostic[..Math.Min(diagnostic.Length, 2_000)], now, attemptReserved: true);
            }
            delivery.LeaseToken = null;
            delivery.LeaseExpiresAt = null;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
}
