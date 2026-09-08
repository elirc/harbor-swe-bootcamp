using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Harbor.Tests.Unit;

public class EditorialPersistenceTests
{
    [Fact]
    public async Task ExistingArticleGetsTokenAndStaleEditCannotLeaveAnOrphanRevision()
    {
        var connection = $"Data Source=editorial-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var anchor = new SqliteConnection(connection);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<HarborDbContext>().UseSqlite(connection).Options;
        var ws = Guid.NewGuid();
        var collection = Guid.NewGuid();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var upgrade = new HarborDbContext(options))
        {
            await upgrade.GetService<IMigrator>().MigrateAsync("20260908191830_M07M11TransfersAndContactVersions");
            await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id, Name, CreatedAt) VALUES ({ws}, {"Existing workspace"}, {now.UtcTicks})");
            await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ArticleCollections (Id, WorkspaceId, Name, Slug, Description, CreatedAt) VALUES ({collection}, {ws}, {"Guides"}, {"guides"}, {""}, {now.UtcTicks})");
            await upgrade.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Articles (Id, WorkspaceId, CollectionId, Title, Slug, Body, Status, CreatedAt, UpdatedAt) VALUES ({id}, {ws}, {collection}, {"Original title"}, {"original"}, {"Original body"}, {0}, {now.UtcTicks}, {now.UtcTicks})");
            await upgrade.Database.MigrateAsync();
        }
        await using var first = new HarborDbContext(options);
        await using var stale = new HarborDbContext(options);
        var a = await first.Articles.SingleAsync(x => x.Id == id);
        var b = await stale.Articles.SingleAsync(x => x.Id == id);
        Assert.NotEqual(Guid.Empty, a.Version);
        Assert.Equal("Original body", a.Body);
        ArticleHistory.Snapshot(first, a, ArticleRevisionReason.Edit, Guid.NewGuid(), now);
        a.Body = "Accepted body";
        await first.SaveChangesAsync();
        ArticleHistory.Snapshot(stale, b, ArticleRevisionReason.Edit, Guid.NewGuid(), now);
        b.Body = "Stale body";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var read = new HarborDbContext(options);
        Assert.Equal("Accepted body", (await read.Articles.SingleAsync()).Body);
        Assert.Equal("Original body", (await read.ArticleRevisions.SingleAsync()).Body);
        Assert.Empty(await read.ArticleFeedback.ToListAsync());
    }
}
