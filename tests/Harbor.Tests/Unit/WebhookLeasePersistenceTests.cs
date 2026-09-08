using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Tests.Unit;

public class WebhookLeasePersistenceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Sender : IWebhookSender
    {
        public Func<WebhookDelivery, Task<WebhookSendResult>> Send = _ => Task.FromResult(WebhookSendResult.Ok(204));
        public Task<WebhookSendResult> SendAsync(WebhookSubscription subscription, WebhookDelivery delivery,
            DateTimeOffset now, CancellationToken cancellationToken = default) => Send(delivery);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"harbor-leases-{Guid.NewGuid():N}.db");
        public DbContextOptions<HarborDbContext> Options { get; }
        public Guid WorkspaceId;
        public Guid DeliveryId;
        public readonly Clock Clock = new();
        public Fixture() => Options = new DbContextOptionsBuilder<HarborDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=1").Options;
        public async Task Seed(int oldAttempts = 0)
        {
            await using var db = new HarborDbContext(Options);
            await db.Database.MigrateAsync();
            var workspace = new Workspace { Name = "Lease test" };
            var subscription = new WebhookSubscription { WorkspaceId = workspace.Id, Url = "https://example.test/hooks", Secret = "private" };
            var delivery = new WebhookDelivery { WorkspaceId = workspace.Id, SubscriptionId = subscription.Id,
                EventType = WebhookEventType.MessageCreated, Payload = "{}", NextAttemptAt = Clock.Now,
                CreatedAt = Clock.Now, AttemptCount = oldAttempts };
            db.AddRange(workspace, subscription, delivery);
            await db.SaveChangesAsync();
            WorkspaceId = workspace.Id; DeliveryId = delivery.Id;
        }
        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task S03_CompetingWorkersCannotAcquireTheSameUnexpiredLease()
    {
        await using var f = new Fixture();
        await f.Seed();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new Sender { Send = async _ =>
        {
            await using var inspection = new HarborDbContext(f.Options);
            var row = await inspection.WebhookDeliveries.SingleAsync();
            Assert.NotNull(row.LeaseToken);
            Assert.Equal(1, row.AttemptCount);
            Assert.Equal(WebhookAttemptOutcome.Started, (await inspection.WebhookAttempts.SingleAsync()).Outcome);
            entered.SetResult();
            await release.Task;
            return WebhookSendResult.Ok(204);
        } };
        var a = new WebhookDispatcher(f.Options, sender, f.Clock);
        var b = new WebhookDispatcher(f.Options, new Sender(), f.Clock);
        var pending = a.DispatchAsync(f.WorkspaceId, f.Clock.Now, limit: 1);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Empty(await b.DispatchAsync(f.WorkspaceId, f.Clock.Now, limit: 1));
        }
        finally { release.TrySetResult(); }
        Assert.Single(await pending);
        await using var read = new HarborDbContext(f.Options);
        Assert.Equal(WebhookAttemptOutcome.Succeeded, (await read.WebhookAttempts.SingleAsync()).Outcome);
        Assert.Null((await read.WebhookDeliveries.SingleAsync()).LeaseToken);
    }

    [Fact]
    public async Task S03S04_ExpiredOwnerCannotOverwriteTheNewOwnerAndHistoryExplainsUncertainty()
    {
        await using var f = new Fixture();
        await f.Seed();
        var a = new WebhookDispatcher(f.Options, new Sender(), f.Clock);
        var b = new WebhookDispatcher(f.Options, new Sender(), f.Clock);
        var old = (await a.ClaimNextAsync(f.WorkspaceId, f.Clock.Now))!;
        Assert.Null(await b.ClaimNextAsync(f.WorkspaceId, f.Clock.Now.AddSeconds(59)));
        f.Clock.Now = f.Clock.Now.AddSeconds(60);
        var current = (await b.ClaimNextAsync(f.WorkspaceId, f.Clock.Now))!;
        Assert.Equal(old.Generation + 1, current.Generation);
        Assert.False(await a.CompleteAsync(old, WebhookSendResult.Ok(200), f.Clock.Now));
        Assert.True(await b.CompleteAsync(current, WebhookSendResult.Rejected(500), f.Clock.Now));
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        var final = (await a.ClaimNextAsync(f.WorkspaceId, f.Clock.Now))!;
        Assert.True(await a.CompleteAsync(final, WebhookSendResult.Ok(204), f.Clock.Now));
        await using var read = new HarborDbContext(f.Options);
        var history = await read.WebhookAttempts.OrderBy(x => x.AttemptNumber).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, history.Select(x => x.AttemptNumber));
        Assert.Equal(new[] { WebhookAttemptOutcome.Unknown, WebhookAttemptOutcome.Rejected, WebhookAttemptOutcome.Succeeded }, history.Select(x => x.Outcome));
        Assert.Equal("LeaseExpired", history[0].FailureCode);
        Assert.All(history, x => Assert.NotNull(x.FinishedAt));
        Assert.Equal(3, (await read.WebhookDeliveries.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task S04_CrashReservationsExhaustBudgetAndLegacyCountsDoNotInventHistory()
    {
        await using var f = new Fixture();
        await f.Seed(oldAttempts: 2);
        var worker = new WebhookDispatcher(f.Options, new Sender(), f.Clock);
        for (var attempt = 3; attempt <= 5; attempt++)
        {
            var claim = (await worker.ClaimNextAsync(f.WorkspaceId, f.Clock.Now))!;
            Assert.True(claim.ShouldSend);
            Assert.Equal(attempt, claim.Delivery.AttemptCount);
            f.Clock.Now = f.Clock.Now.AddSeconds(61);
        }
        Assert.False((await worker.ClaimNextAsync(f.WorkspaceId, f.Clock.Now))!.ShouldSend);
        Assert.Null(await worker.ClaimNextAsync(f.WorkspaceId, f.Clock.Now));
        await using var read = new HarborDbContext(f.Options);
        Assert.Equal(WebhookDeliveryStatus.Failed, (await read.WebhookDeliveries.SingleAsync()).Status);
        var history = await read.WebhookAttempts.OrderBy(x => x.AttemptNumber).ToListAsync();
        Assert.Equal(new[] { 3, 4, 5 }, history.Select(x => x.AttemptNumber));
        Assert.All(history, a => Assert.Equal(WebhookAttemptOutcome.Unknown, a.Outcome));
    }

    [Fact]
    public async Task S04_CancellationAfterTransportBeginsLeavesRecoverableUncertainty()
    {
        await using var f = new Fixture();
        await f.Seed();
        using var cancellation = new CancellationTokenSource();
        var sender = new Sender { Send = _ =>
        {
            cancellation.Cancel();
            return Task.FromResult(WebhookSendResult.Ok(204));
        } };
        var worker = new WebhookDispatcher(f.Options, sender, f.Clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.DispatchAsync(f.WorkspaceId, f.Clock.Now, cancellation.Token, 1));
        await using (var read = new HarborDbContext(f.Options))
        {
            Assert.Equal(WebhookAttemptOutcome.Started, (await read.WebhookAttempts.SingleAsync()).Outcome);
            Assert.NotNull((await read.WebhookDeliveries.SingleAsync()).LeaseToken);
        }
        f.Clock.Now = f.Clock.Now.AddSeconds(60);
        Assert.Equal(2, (await worker.ClaimNextAsync(f.WorkspaceId, f.Clock.Now))!.Delivery.AttemptCount);
        await using var recovered = new HarborDbContext(f.Options);
        Assert.Equal(WebhookAttemptOutcome.Unknown, (await recovered.WebhookAttempts.SingleAsync(a => a.AttemptNumber == 1)).Outcome);
    }
}
