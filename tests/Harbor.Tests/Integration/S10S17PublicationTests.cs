using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tests.Integration;

public class S10S17PublicationTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<CollectionResponse> Collection(Guid ws, string name) => await ReadAsync<CollectionResponse>(
        await Client.PostAsJsonAsync($"/api/workspaces/{ws}/collections", new CreateCollectionRequest(name), Json));
    private async Task<ArticleResponse> Draft(Guid ws, Guid collection, string title = "Refunds") => await ReadAsync<ArticleResponse>(
        await Client.PostAsJsonAsync($"/api/workspaces/{ws}/articles", new CreateArticleRequest(collection, title, "Approved A"), Json));
    private Task<HttpResponseMessage> Publish(Guid id) => Client.PostAsync($"/api/articles/{id}/publish", null);
    private Task<HttpResponseMessage> Schedule(ArticleResponse article, DateTimeOffset at) => Client.PutAsJsonAsync(
        $"/api/articles/{article.Id}/publication-schedule", new SetPublicationScheduleRequest(article.Version, at), Json);
    private async Task<int> Process(DateTimeOffset at)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PublicationScheduler>().ProcessDueAsync(at);
    }

    [Fact]
    public async Task PrivateEditsKeepOldContentRouteAndCollectionUntilRepublished()
    {
        var ws = await CreateWorkspaceAsync();
        var oldCollection = await Collection(ws.Id, "Old");
        var nextCollection = await Collection(ws.Id, "Next");
        var article = await Draft(ws.Id, oldCollection.Id);
        var first = await ReadAsync<ArticleResponse>(await Publish(article.Id));
        Assert.False(first.HasUnpublishedChanges);
        var edited = await ReadAsync<ArticleResponse>(await Client.PutAsJsonAsync($"/api/articles/{article.Id}",
            new UpdateArticleRequest(nextCollection.Id, "Private title", "Secret B", "refunds-new"), Json));
        Assert.True(edited.HasUnpublishedChanges);
        var live = await ReadAsync<PublicArticleResponse>(await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles/refunds"));
        Assert.Equal("Approved A", live.Body);
        Assert.Equal(oldCollection.Id, live.CollectionId);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles/refunds-new")).StatusCode);
        Assert.Empty(await ReadAsync<List<PublicArticleResponse>>(await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles?q=Secret")));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/collections/{oldCollection.Id}")).StatusCode);
        var next = await ReadAsync<ArticleResponse>(await Publish(article.Id));
        Assert.False(next.HasUnpublishedChanges);
        Assert.Equal(first.PublishedAt, next.PublishedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles/refunds")).StatusCode);
        Assert.Equal("Secret B", (await ReadAsync<PublicArticleResponse>(await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles/refunds-new"))).Body);
        (await Publish(article.Id)).EnsureSuccessStatusCode();
        Factory.WithDb(db => Assert.Equal(2, db.ArticlePublicationSnapshots.Count(s => s.ArticleId == article.Id)));
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/articles/{article.Id}")).StatusCode);
        Factory.WithDb(db => { Assert.Empty(db.ArticlePublicationSnapshots.Where(s => s.ArticleId == article.Id)); Assert.Empty(db.ActiveArticlePublications.Where(s => s.ArticleId == article.Id)); });
    }

    [Fact]
    public async Task SlugConflictAndFailedSnapshotInsertPreserveOldPublication()
    {
        var ws = await CreateWorkspaceAsync();
        var collection = await Collection(ws.Id, "Guides");
        var a = await Draft(ws.Id, collection.Id);
        (await Publish(a.Id)).EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/articles/{a.Id}", new UpdateArticleRequest(collection.Id, a.Title, "B", "new-route"), Json)).EnsureSuccessStatusCode();
        var b = await Draft(ws.Id, collection.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Publish(b.Id)).StatusCode);
        Factory.FailCommand = command => command.CommandText.Contains("INSERT INTO \"ArticlePublicationSnapshots\"");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Publish(a.Id)).StatusCode); }
        finally { Factory.FailCommand = null; }
        Assert.Equal("Approved A", (await ReadAsync<PublicArticleResponse>(await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles/refunds"))).Body);
        Factory.WithDb(db => Assert.Single(db.ArticlePublicationSnapshots.Where(s => s.ArticleId == a.Id)));
        (await Publish(a.Id)).EnsureSuccessStatusCode();
        (await Publish(b.Id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DueBoundaryUsesDurableScheduleAndPublishesOnce()
    {
        var ws = await CreateWorkspaceAsync();
        var article = await Draft(ws.Id, (await Collection(ws.Id, "Guides")).Id);
        var now = DateTimeOffset.UtcNow;
        Factory.FrozenTime = now;
        try
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Schedule(article, now)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Schedule(article, now.AddDays(91))).StatusCode);
            var schedule = await ReadAsync<PublicationScheduleResponse>(await Schedule(article, now.AddMinutes(1)));
            Assert.Equal(article.Version, schedule.ArticleVersion);
            Assert.Equal(0, await Process(now.AddMinutes(1).AddTicks(-1)));
            Assert.Equal(1, await Process(now.AddMinutes(1)));
            Assert.Equal(0, await Process(now.AddMinutes(2)));
            var read = await ReadAsync<PublicationScheduleResponse>(await Client.GetAsync($"/api/articles/{article.Id}/publication-schedule"));
            Assert.Equal(PublicationScheduleStatus.Completed, read.Schedule!.Status);
            Assert.NotEqual(article.Version, read.ArticleVersion);
            Factory.WithDb(db => Assert.Single(db.ArticlePublicationSnapshots.Where(s => s.ArticleId == article.Id)));
        }
        finally { Factory.FrozenTime = null; }
    }

    [Fact]
    public async Task EditsAndLostAuthorityCannotPublishUnapprovedContent()
    {
        var ws = await CreateWorkspaceAsync();
        var collection = await Collection(ws.Id, "Guides");
        var a = await Draft(ws.Id, collection.Id);
        var b = await Draft(ws.Id, collection.Id, "Shipping");
        var at = DateTimeOffset.UtcNow.AddHours(1);
        (await Schedule(a, at)).EnsureSuccessStatusCode();
        (await Schedule(b, at)).EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/articles/{a.Id}", new UpdateArticleRequest(collection.Id, a.Title, "Unreviewed", a.Slug), Json)).EnsureSuccessStatusCode();
        Factory.WithDb(db => { var admin = db.Teammates.Single(t => t.WorkspaceId == ws.Id); admin.Role = TeammateRole.Agent; db.SaveChanges(); });
        Assert.Equal(1, await Process(at));
        Factory.WithDb(db =>
        {
            Assert.All(db.ArticlePublicationSchedules.Where(s => s.WorkspaceId == ws.Id), s => Assert.Equal(PublicationScheduleStatus.NeedsReview, s.Status));
            Assert.Empty(db.ActiveArticlePublications.Where(s => s.WorkspaceId == ws.Id));
        });
    }

    [Fact]
    public async Task FixedClockRepublishAdvancesVersionAndFailedScheduleCommitRollsBack()
    {
        var ws = await CreateWorkspaceAsync(); var collection = await Collection(ws.Id, "Guides");
        var now = DateTimeOffset.UtcNow; Factory.FrozenTime = now;
        try
        {
            var a = await Draft(ws.Id, collection.Id);
            (await Publish(a.Id)).EnsureSuccessStatusCode();
            var edited = await ReadAsync<ArticleResponse>(await Client.PutAsJsonAsync($"/api/articles/{a.Id}", new UpdateArticleRequest(collection.Id, a.Title, "B", a.Slug), Json));
            var published = await ReadAsync<ArticleResponse>(await Publish(a.Id));
            Assert.NotEqual(edited.Version, published.Version);
            var scheduled = await Draft(ws.Id, collection.Id, "Scheduled");
            (await Schedule(scheduled, now.AddMinutes(1))).EnsureSuccessStatusCode();
            var hit = false;
            Factory.FailCommand = command => hit = command.CommandText.Contains("UPDATE \"ArticlePublicationSchedules\"");
            try { await Assert.ThrowsAnyAsync<Exception>(() => Process(now.AddMinutes(1))); }
            finally { Factory.FailCommand = null; }
            Assert.True(hit);
            Factory.WithDb(db =>
            {
                Assert.Equal(PublicationScheduleStatus.Pending, db.ArticlePublicationSchedules.Single(s => s.ArticleId == scheduled.Id).Status);
                Assert.Empty(db.ActiveArticlePublications.Where(p => p.ArticleId == scheduled.Id));
                Assert.Empty(db.ArticlePublicationSnapshots.Where(p => p.ArticleId == scheduled.Id));
            });
            Assert.Equal(1, await Process(now.AddMinutes(1)));
        }
        finally { Factory.FrozenTime = null; }
    }
}
