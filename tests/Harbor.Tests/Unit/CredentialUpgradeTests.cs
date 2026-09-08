using Harbor.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Harbor.Tests.Unit;

public class CredentialUpgradeTests
{
    [Fact]
    public async Task ExistingHashBackfillsOnceAndSeedingDoesNotResurrectRevocation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HarborDbContext>().UseSqlite(connection).Options;
        await using var db = new HarborDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20260908194406_M23M25M29EditorialAndWebhookRecovery");
        var workspace = Guid.NewGuid();
        var teammate = Guid.NewGuid();
        var raw = "hbk_known_upgrade_fixture";
        var hash = ApiKeys.Hash(raw);
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id, Name, CreatedAt) VALUES ({workspace}, {"Upgrade"}, {now.UtcTicks})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Teammates (Id, WorkspaceId, Name, Email, Role, Availability, ApiKeyHash, CreatedAt) VALUES ({teammate}, {workspace}, {"Owner"}, {"owner@example.test"}, {1}, {0}, {hash}, {now.UtcTicks})");
        await db.Database.MigrateAsync();
        var credential = await db.TeammateApiKeys.SingleAsync();
        Assert.Equal(teammate, credential.Id);
        Assert.Equal(hash, credential.Hash);
        Assert.Null(credential.ExpiresAt);
        credential.RevokedAt = now;
        await db.SaveChangesAsync();
        DataSeeder.Seed(db);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(now, (await db.TeammateApiKeys.SingleAsync()).RevokedAt);
    }
}
