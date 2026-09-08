using Harbor.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Harbor.Tests.Unit;

public class PublicationUpgradeTests
{
    [Fact]
    public async Task LegacySharedInitialTokensBecomeDistinctBeforeScheduling()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HarborDbContext(new DbContextOptionsBuilder<HarborDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260908203747_S06ToS09ContactWorkflowsRoutingAndWatches");
        var ws = Guid.NewGuid(); var collection = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var now = DateTimeOffset.UtcNow.UtcTicks;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id,Name,CreatedAt) VALUES ({ws},{"Legacy"},{now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ArticleCollections (Id,WorkspaceId,Name,Slug,CreatedAt) VALUES ({collection},{ws},{"Guides"},{"guides"},{now})");
        foreach (var id in new[] { first, second })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Articles (Id,WorkspaceId,CollectionId,Title,Slug,Body,Status,CreatedAt,UpdatedAt) VALUES ({id},{ws},{collection},{"Draft"},{id.ToString()},{"Body"},{0},{now},{now})");
        await db.Database.MigrateAsync();
        var rows = await db.Articles.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, rows.Select(a => a.Version).Distinct().Count());
        Assert.All(rows, a => Assert.Equal(a.Id, a.Version));
    }

    [Fact]
    public async Task ExistingLiveContentAndFirstPublicationSurviveUpgradeWhileDraftStaysPrivate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HarborDbContext>().UseSqlite(connection).Options;
        await using var db = new HarborDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20260908203747_S06ToS09ContactWorkflowsRoutingAndWatches");
        var ws = Guid.NewGuid(); var collection = Guid.NewGuid(); var liveId = Guid.NewGuid(); var draftId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow; var published = now.AddDays(-10); var version = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id,Name,CreatedAt) VALUES ({ws},{"Legacy"},{now.UtcTicks})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ArticleCollections (Id,WorkspaceId,Name,Slug,CreatedAt) VALUES ({collection},{ws},{"Guides"},{"guides"},{now.UtcTicks})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Articles (Id,WorkspaceId,CollectionId,Title,Slug,Body,Status,CreatedAt,UpdatedAt,PublishedAt,Version) VALUES ({liveId},{ws},{collection},{"Approved"},{"approved"},{"Old live body"},{1},{published.UtcTicks},{now.UtcTicks},{published.UtcTicks},{version})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Articles (Id,WorkspaceId,CollectionId,Title,Slug,Body,Status,CreatedAt,UpdatedAt,Version) VALUES ({draftId},{ws},{collection},{"Private"},{"private"},{"Draft body"},{0},{now.UtcTicks},{now.UtcTicks},{Guid.NewGuid()})");
        await db.Database.MigrateAsync();
        var live = Assert.Single(await ArticlePublications.Public(db, ws).ToListAsync());
        Assert.Equal(liveId, live.ArticleId); Assert.Equal("Old live body", live.Body);
        Assert.Equal("approved", live.Slug); Assert.Equal(collection, live.CollectionId);
        Assert.Equal(published, live.FirstPublishedAt); Assert.Equal(now, live.UpdatedAt);
        Assert.False((await db.Articles.SingleAsync(a => a.Id == liveId)).HasUnpublishedChanges);
        Assert.Empty(await db.ActiveArticlePublications.Where(a => a.ArticleId == draftId).ToListAsync());
    }
}
