using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class PersonalWorkLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M01_SavedQueueIsPrivateAndReevaluatesCurrentData()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var a = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var b = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var other = await CreateTeammateAsync(workspace.Id);
        var root = $"/api/workspaces/{workspace.Id}/saved-queues";
        var queue = await ReadAsync<SavedQueueResponse>(await Client.PostAsJsonAsync(root,
            new { name = "  Open work  ", filters = new { state = "Open", page = 99, pageSize = 1 } }));
        Assert.Equal("Open work", queue.Name);
        Factory.WithDb(db => Assert.DoesNotContain("page", db.SavedQueues.Single(q => q.Id == queue.Id).FilterJson, StringComparison.OrdinalIgnoreCase));
        var route = $"/api/saved-queues/{queue.Id}";
        var first = await Client.GetAsync(route + "/conversations?pageSize=1");
        var firstRow = Assert.Single(await ReadAsync<List<ConversationSummaryResponse>>(first));
        Assert.Equal("2", Assert.Single(first.Headers.GetValues("X-Total-Count")));
        var secondRow = Assert.Single(await ReadAsync<List<ConversationSummaryResponse>>(
            await Client.GetAsync(route + "/conversations?pageSize=1&page=2")));
        Assert.NotEqual(firstRow.Id, secondRow.Id);
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{a.Id}/state",
            new ChangeStateRequest(ConversationState.Closed, null), Json));
        Assert.Equal(b.Id, Assert.Single(await ReadAsync<List<ConversationSummaryResponse>>(
            await Client.GetAsync(route + "/conversations"))).Id);
        ActAs(other.ApiKey);
        Assert.Empty(await ReadAsync<List<SavedQueueResponse>>(await Client.GetAsync(root)));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync(route)).StatusCode);
        ActAsAdminOf(workspace.Id);
        await ReadAsync<SavedQueueResponse>(await Client.PutAsJsonAsync(route,
            new SaveQueueRequest("Missing segment", new SavedQueueFilter { SegmentId = Guid.NewGuid() }), Json));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.GetAsync(route + "/conversations")).StatusCode);
        Factory.WithDb(db => { db.SavedQueues.Single(q => q.Id == queue.Id).SchemaVersion = 99; db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.GetAsync(route + "/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync(route)).StatusCode);
    }

    [Theory]
    [InlineData("{\"name\":\"Bad\",\"filters\":{\"state\":999}}")]
    [InlineData("{\"name\":\"Bad\",\"filters\":{\"createdFrom\":\"2026-02-01\",\"createdBefore\":\"2026-01-01\"}}")]
    [InlineData("{\"name\":\"   \",\"filters\":{}}")]
    [InlineData("{\"name\":\"Missing filters\"}")]
    public async Task M01_InvalidRecipeIsRejected(string json)
    {
        var workspace = await CreateWorkspaceAsync();
        var response = await Client.PostAsync($"/api/workspaces/{workspace.Id}/saved-queues",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task M02_BookmarksAreIndependentIdempotentAndDoNotTouchConversation()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var conversation = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var other = await CreateTeammateAsync(workspace.Id);
        var route = $"/api/conversations/{conversation.Id}/bookmark";
        var list = $"/api/workspaces/{workspace.Id}/bookmarked-conversations";
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync(route, null)).StatusCode);
        ActAs(other.ApiKey);
        Assert.Empty(await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(list)));
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync(route, null)).StatusCode);
        ActAsAdminOf(workspace.Id);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync(route)).StatusCode);
        Assert.Empty(await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(list)));
        ActAs(other.ApiKey);
        Assert.Equal(conversation.Id, Assert.Single(await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(list))).Id);
        var after = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        Assert.Equal(conversation.UpdatedAt, after.UpdatedAt);
        Assert.Equal(conversation.LastMessageAt, after.LastMessageAt);
        Assert.Equal(conversation.AssignedTeammateId, after.AssignedTeammateId);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync(route)).StatusCode);
    }

    [Fact]
    public async Task M03_TwoTabsCannotOverwriteANewerPrivateDraft()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var conversation = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var other = await CreateTeammateAsync(workspace.Id);
        var route = $"/api/conversations/{conversation.Id}/my-draft";
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
        var createdResponse = await Client.PutAsJsonAsync(route, new SaveDraftRequest("Checking now", null));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var v1 = await ReadAsync<DraftResponse>(createdResponse);
        var v2 = await ReadAsync<DraftResponse>(await Client.PutAsJsonAsync(route, new SaveDraftRequest("Found the answer", v1.Version)));
        Assert.NotEqual(v1.Version, v2.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(route, new SaveDraftRequest("Stale tab", v1.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(route, new SaveDraftRequest("Create again", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync(route + $"?expectedVersion={v1.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.DeleteAsync(route)).StatusCode);
        Assert.Equal(v2, await ReadAsync<DraftResponse>(await Client.GetAsync(route)));
        ActAs(other.ApiKey);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
        var own = await ReadAsync<DraftResponse>(await Client.PutAsJsonAsync(route, new SaveDraftRequest("Need logs", null)));
        Assert.Equal("Need logs", own.Body);
        ActAsAdminOf(workspace.Id);
        Assert.Equal(v2, await ReadAsync<DraftResponse>(await Client.GetAsync(route)));
        var after = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        Assert.Equal(conversation.UpdatedAt, after.UpdatedAt);
        Assert.Equal(conversation.LastMessageAt, after.LastMessageAt);
        Assert.Null(after.FirstRespondedAt);
        Assert.Single(after.Messages);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync(route + $"?expectedVersion={v2.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
        ActAs(other.ApiKey);
        Assert.Equal(own, await ReadAsync<DraftResponse>(await Client.GetAsync(route)));
    }
}
