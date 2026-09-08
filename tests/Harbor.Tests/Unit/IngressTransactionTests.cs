using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Tests.Unit;

public class IngressTransactionTests
{
    [Fact]
    public async Task IndependentWritersCommitOneReceiptAndAThrownCallbackRollsBackIntermediateSave()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harbor-ingress-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HarborDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=1").Options;
        try
        {
            Guid workspaceId;
            Guid teammateId;
            await using (var setup = new HarborDbContext(options))
            {
                await setup.Database.MigrateAsync();
                var workspace = new Workspace { Name = "Receipts" };
                var teammate = new Teammate { WorkspaceId = workspace.Id, Name = "Owner", Email = "owner@example.test", ApiKeyHash = "test-hash" };
                setup.AddRange(workspace, teammate);
                await setup.SaveChangesAsync();
                workspaceId = workspace.Id;
                teammateId = teammate.Id;
            }
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrived = 0;
            async Task<Guid> Compete()
            {
                await using var db = new HarborDbContext(options);
                if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
                await gate.Task;
                return await WriteTransactions.RunAsync(db, async () =>
                {
                    var existing = await db.CreationReceipts.SingleOrDefaultAsync(r => r.WorkspaceId == workspaceId && r.Key == "same");
                    if (existing is not null) return existing.Id;
                    var receipt = Receipt(workspaceId, teammateId, "same");
                    db.CreationReceipts.Add(receipt);
                    await db.SaveChangesAsync();
                    return receipt.Id;
                });
            }
            var results = await Task.WhenAll(Task.Run(Compete), Task.Run(Compete));
            Assert.Equal(results[0], results[1]);
            await using (var rollback = new HarborDbContext(options))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => WriteTransactions.RunAsync<int>(rollback, async () =>
                {
                    rollback.CreationReceipts.Add(Receipt(workspaceId, teammateId, "rollback"));
                    await rollback.SaveChangesAsync();
                    throw new InvalidOperationException("Injected failure after intermediate save");
                }));
            }
            await using var read = new HarborDbContext(options);
            Assert.Equal("same", (await read.CreationReceipts.SingleAsync()).Key);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private static CreationReceipt Receipt(Guid workspaceId, Guid teammateId, string key) => new()
    {
        WorkspaceId = workspaceId, TeammateId = teammateId, Key = key, RequestHash = "hash",
        ResponseJson = "{}", Location = "/api/conversations/example", CreatedAt = DateTimeOffset.UtcNow,
        RetainUntil = DateTimeOffset.UtcNow.AddHours(24),
    };
}
