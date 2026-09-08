using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class ArticleLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<CollectionResponse> Collection(Guid workspace, string name = "Guides") =>
        await ReadAsync<CollectionResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{workspace}/collections", new CreateCollectionRequest(name), Json));
    private async Task<ArticleResponse> Article(Guid workspace, Guid collection, string title, string body = "Refund guidance") =>
        await ReadAsync<ArticleResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{workspace}/articles", new CreateArticleRequest(collection, title, body), Json));

    [Fact]
    public async Task M22_M23_CloneResetsPublicationAndRevisionsCaptureStateBeforeChanges()
    {
        var ws = await CreateWorkspaceAsync();
        var collection = await Collection(ws.Id);
        var article = await Article(ws.Id, collection.Id, "Refunds", "Body A");
        var route = $"/api/articles/{article.Id}";
        Assert.Empty(await ReadAsync<List<ArticleRevisionMetadata>>(await Client.GetAsync(route + "/revisions")));
        var edited = await ReadAsync<ArticleResponse>(await Client.PutAsJsonAsync(route,
            new UpdateArticleRequest(collection.Id, article.Title, "Body B", article.Slug), Json));
        await ReadAsync<ArticleResponse>(await Client.PutAsJsonAsync(route,
            new UpdateArticleRequest(collection.Id, article.Title, "Body B", article.Slug), Json));
        var published = await ReadAsync<ArticleResponse>(await Client.PostAsync(route + "/publish", null));
        await ReadAsync<ArticleResponse>(await Client.PostAsync(route + "/publish", null));
        var clone = await ReadAsync<ArticleResponse>(await Client.PostAsJsonAsync(route + "/clone",
            new CloneArticleRequest(collection.Id, "Refunds for teams"), Json));
        Assert.NotEqual(article.Id, clone.Id);
        Assert.Equal(ArticleStatus.Draft, clone.Status);
        Assert.Null(clone.PublishedAt);
        Assert.Equal("Body B", clone.Body);
        Assert.Equal("refunds-for-teams", clone.Slug);
        var publicRoute = $"/api/public/workspaces/{ws.Id}/articles/";
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(publicRoute + clone.Slug)).StatusCode);
        Assert.Equal("Body B", (await ReadAsync<PublicArticleResponse>(await Client.GetAsync(publicRoute + article.Slug))).Body);
        await ReadAsync<ArticleResponse>(await Client.PutAsJsonAsync($"/api/articles/{clone.Id}",
            new UpdateArticleRequest(collection.Id, clone.Title, "Teams-only adaptation", clone.Slug), Json));
        Assert.Equal("Body B", (await ReadAsync<PublicArticleResponse>(await Client.GetAsync(publicRoute + article.Slug))).Body);
        await ReadAsync<ArticleResponse>(await Client.PostAsync(route + "/unpublish", null));
        var revisions = await ReadAsync<List<ArticleRevisionMetadata>>(await Client.GetAsync(route + "/revisions"));
        Assert.Equal(3, revisions.Count);
        var snapshots = new Dictionary<ArticleRevisionReason, ArticleRevisionResponse>();
        foreach (var revision in revisions)
            snapshots[revision.Reason] = await ReadAsync<ArticleRevisionResponse>(await Client.GetAsync(route + $"/revisions/{revision.Id}"));
        Assert.Equal("Body A", snapshots[ArticleRevisionReason.Edit].Body);
        Assert.Equal(ArticleStatus.Draft, snapshots[ArticleRevisionReason.Publish].Status);
        Assert.Equal("Body B", snapshots[ArticleRevisionReason.Publish].Body);
        Assert.Equal(ArticleStatus.Published, snapshots[ArticleRevisionReason.Unpublish].Status);
        Assert.Equal(published.PublishedAt, snapshots[ArticleRevisionReason.Unpublish].PublishedAt);
        var page = await Client.GetAsync(route + "/revisions?pageSize=1&page=2");
        Assert.Single(await ReadAsync<List<ArticleRevisionMetadata>>(page));
        Assert.Equal("3", Assert.Single(page.Headers.GetValues("X-Total-Count")));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(route,
            new UpdateArticleRequest(collection.Id, article.Title, "Rejected", clone.Slug), Json)).StatusCode);
        Assert.Equal(3, (await ReadAsync<List<ArticleRevisionMetadata>>(await Client.GetAsync(route + "/revisions"))).Count);
        var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(route + "/revisions")).StatusCode);
        ActAsAdminOf(ws.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync(route)).StatusCode);
        Factory.WithDb(db => Assert.Empty(db.ArticleRevisions.Where(r => r.ArticleId == article.Id)));
    }

    [Fact]
    public async Task M24_RelatedArticlesArePublicScopedAndRankedWithinTheirCollection()
    {
        var ws = await CreateWorkspaceAsync();
        var collection = await Collection(ws.Id);
        var otherCollection = await Collection(ws.Id, "Other");
        var source = await Article(ws.Id, collection.Id, "Refund source", "Refund");
        var wanted = await Article(ws.Id, collection.Id, "Refund guide", "Refund");
        var draft = await Article(ws.Id, collection.Id, "Refund draft", "Refund");
        var outside = await Article(ws.Id, otherCollection.Id, "Refund outside", "Refund");
        foreach (var article in new[] { source, wanted, outside })
            await ReadAsync<ArticleResponse>(await Client.PostAsync($"/api/articles/{article.Id}/publish", null));
        Client.DefaultRequestHeaders.Remove("X-Api-Key");
        var route = $"/api/public/workspaces/{ws.Id}/articles/{source.Slug}/related";
        var found = Assert.Single(await ReadAsync<List<SuggestedArticleResponse>>(await Client.GetAsync(route)));
        Assert.Equal(wanted.Id, found.Id);
        Assert.Equal(4, found.Score);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + "?limit=11")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/public/workspaces/{ws.Id}/articles/{draft.Slug}/related")).StatusCode);
    }

    [Fact]
    public async Task M25_StaffVotesReplaceOpinionsWithoutAddingVoters()
    {
        var ws = await CreateWorkspaceAsync();
        var collection = await Collection(ws.Id);
        var article = await Article(ws.Id, collection.Id, "Staff draft");
        var agent = await CreateTeammateAsync(ws.Id);
        var route = $"/api/articles/{article.Id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(route + "/my-feedback", new { })).StatusCode);
        await ReadAsync<ArticleFeedbackResponse>(await Client.PutAsJsonAsync(route + "/my-feedback", new SetArticleFeedbackRequest(true)));
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route + "/my-feedback")).StatusCode);
        await ReadAsync<ArticleFeedbackResponse>(await Client.PutAsJsonAsync(route + "/my-feedback", new SetArticleFeedbackRequest(false)));
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(route + "/feedback-summary")).StatusCode);
        ActAsAdminOf(ws.Id);
        Assert.Equal(new ArticleFeedbackSummary(2, 1, 1), await ReadAsync<ArticleFeedbackSummary>(await Client.GetAsync(route + "/feedback-summary")));
        await ReadAsync<ArticleFeedbackResponse>(await Client.PutAsJsonAsync(route + "/my-feedback", new SetArticleFeedbackRequest(false)));
        Assert.Equal(new ArticleFeedbackSummary(2, 0, 2), await ReadAsync<ArticleFeedbackSummary>(await Client.GetAsync(route + "/feedback-summary")));
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync(route + "/my-feedback")).StatusCode);
        Assert.Equal(new ArticleFeedbackSummary(1, 0, 1), await ReadAsync<ArticleFeedbackSummary>(await Client.GetAsync(route + "/feedback-summary")));
        await ReadAsync<ArticleResponse>(await Client.PutAsJsonAsync(route,
            new UpdateArticleRequest(collection.Id, article.Title, "Edited draft", article.Slug), Json));
        Assert.Equal(new ArticleFeedbackSummary(1, 0, 1), await ReadAsync<ArticleFeedbackSummary>(await Client.GetAsync(route + "/feedback-summary")));
    }

    [Fact]
    public async Task M24_CandidateLimitAppliesBeforeRanking()
    {
        var ws = await CreateWorkspaceAsync();
        var collection = await Collection(ws.Id);
        var source = await Article(ws.Id, collection.Id, "Refund", "Refund");
        await ReadAsync<ArticleResponse>(await Client.PostAsync($"/api/articles/{source.Id}/publish", null));
        var now = DateTimeOffset.UtcNow;
        Factory.WithDb(db =>
        {
            db.Articles.Add(new Harbor.Domain.Entities.Article { WorkspaceId = ws.Id, CollectionId = collection.Id,
                Title = "Refund match", Slug = "old-match", Body = "Refund", Status = ArticleStatus.Published, UpdatedAt = now.AddDays(-1) });
            for (var i = 0; i < 200; i++)
                db.Articles.Add(new Harbor.Domain.Entities.Article { WorkspaceId = ws.Id, CollectionId = collection.Id,
                    Title = "Unrelated guide", Slug = "unrelated-" + i, Body = "Different information", Status = ArticleStatus.Published, UpdatedAt = now });
            foreach (var entry in db.ChangeTracker.Entries<Harbor.Domain.Entities.Article>().Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added).ToList())
                Harbor.Infrastructure.ArticlePublications.StageSeedPublication(db, entry.Entity);
            db.SaveChanges();
        });
        Assert.Empty(await ReadAsync<List<SuggestedArticleResponse>>(await Client.GetAsync(
            $"/api/public/workspaces/{ws.Id}/articles/{source.Slug}/related")));
    }
}
