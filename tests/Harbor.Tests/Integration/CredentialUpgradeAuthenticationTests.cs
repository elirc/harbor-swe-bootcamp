using System.Net;
using Harbor.Domain;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Harbor.Tests.Integration;

public class CredentialUpgradeAuthenticationTests
{
    [Fact]
    public async Task S05_PreMigrationRawKeyStillAuthenticatesAndRevocationHasNoLegacyFallback()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harbor-key-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<HarborDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            var workspace = Guid.NewGuid();
            var teammate = Guid.NewGuid();
            var raw = "hbk_previous_release_fixture";
            var hash = ApiKeys.Hash(raw);
            var now = DateTimeOffset.UtcNow.UtcTicks;
            await using (var old = new HarborDbContext(options))
            {
                await old.GetService<IMigrator>().MigrateAsync("20260908194406_M23M25M29EditorialAndWebhookRecovery");
                await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id, Name, CreatedAt) VALUES ({workspace}, {"Upgrade"}, {now})");
                await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Teammates (Id, WorkspaceId, Name, Email, Role, Availability, ApiKeyHash, CreatedAt) VALUES ({teammate}, {workspace}, {"Owner"}, {"owner@example.test"}, {(int)TeammateRole.Admin}, {0}, {hash}, {now})");
            }
            using var factory = new HarborApiFactory { FileDatabasePath = path };
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", raw);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/teammates/{teammate}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/api-keys/{teammate}")).StatusCode);
            factory.SeedDemoData();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/teammates/{teammate}")).StatusCode);
            factory.WithDb(db => Assert.Equal(hash, db.Teammates.Single(t => t.Id == teammate).ApiKeyHash));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
