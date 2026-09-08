using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Domain;
using Harbor.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tests.Integration;

public class SeniorWorkflowConcurrencyTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }

    private sealed class Scenario : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "harbor-senior-race-" + Guid.NewGuid().ToString("N") + ".db");
        public HarborApiFactory Factory { get; }
        public HttpClient A { get; }
        public HttpClient B { get; }
        public CreateWorkspaceResponse Workspace { get; private set; } = null!;
        public InboxResponse Inbox { get; private set; } = null!;
        public ContactResponse Contact { get; private set; } = null!;
        public Scenario() { Factory = new HarborApiFactory { FileDatabasePath = path }; A = Factory.CreateClient(); B = Factory.CreateClient(); }
        public async Task Initialize()
        {
            Workspace = await Read<CreateWorkspaceResponse>(await A.PostAsJsonAsync("/api/workspaces", new CreateWorkspaceRequest("Race", "Owner", "owner@race.test"), Json));
            A.DefaultRequestHeaders.Add("X-Api-Key", Workspace.ApiKey); B.DefaultRequestHeaders.Add("X-Api-Key", Workspace.ApiKey);
            Inbox = await Read<InboxResponse>(await A.PostAsJsonAsync($"/api/workspaces/{Workspace.Workspace.Id}/inboxes", new CreateInboxRequest("Support", null), Json));
            Contact = await Read<ContactResponse>(await A.PostAsJsonAsync($"/api/workspaces/{Workspace.Workspace.Id}/contacts", new CreateContactRequest("Ada", null, null), Json));
        }
        public Task<HttpResponseMessage> Start(HttpClient client) => client.PostAsJsonAsync($"/api/workspaces/{Workspace.Workspace.Id}/conversations", new StartConversationRequest(Inbox.Id, Contact.Id, "Race", "Hello"), Json);
        public void Dispose()
        {
            A.Dispose(); B.Dispose(); Factory.Dispose();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task S06_CompetingMergeCommitsReturnOneStoredResult()
    {
        using var s = new Scenario(); await s.Initialize();
        s.Factory.WithDb(db => { db.Contacts.Single(c => c.Id == s.Contact.Id).Email = "duplicate@race.test"; db.SaveChanges(); });
        var target = await Read<ContactResponse>(await s.A.PostAsJsonAsync($"/api/workspaces/{s.Workspace.Workspace.Id}/contacts", new CreateContactRequest("Target", "duplicate@race.test", null), Json));
        var plan = await Read<ContactMergePlanResponse>(await s.A.PostAsJsonAsync($"/api/workspaces/{s.Workspace.Workspace.Id}/contact-merges/preview", new { sourceId = s.Contact.Id, targetId = target.Id }, Json));
        var route = $"/api/contact-merges/{plan.Id}/commit";
        var results = await Race(() => s.A.PostAsync(route, null), () => s.B.PostAsync(route, null));
        for (var i = 0; i < results.Length; i++) if (results[i].StatusCode == HttpStatusCode.ServiceUnavailable) results[i] = await s.A.PostAsync(route, null);
        Assert.All(results, r => r.EnsureSuccessStatusCode());
        Assert.Equal(await results[0].Content.ReadAsStringAsync(), await results[1].Content.ReadAsStringAsync());
        s.Factory.WithDb(db => { Assert.False(db.Contacts.Any(c => c.Id == s.Contact.Id)); Assert.True(db.Contacts.Any(c => c.Id == target.Id)); });
    }

    [Fact]
    public async Task S07_CompetingImportCommitsDoNotCreateTheRowsTwice()
    {
        using var s = new Scenario(); await s.Initialize();
        var batch = await Read<ContactImportResponse>(await s.A.PostAsJsonAsync($"/api/workspaces/{s.Workspace.Workspace.Id}/contact-imports", new { rows = new[] { new { name = "Imported A" }, new { name = "Imported B" } } }, Json));
        var route = $"/api/contact-imports/{batch.Id}/commit";
        var results = await Race(() => s.A.PostAsync(route, null), () => s.B.PostAsync(route, null));
        for (var i = 0; i < results.Length; i++) if (results[i].StatusCode == HttpStatusCode.ServiceUnavailable) results[i] = await s.A.PostAsync(route, null);
        Assert.All(results, r => r.EnsureSuccessStatusCode());
        Assert.Equal(await results[0].Content.ReadAsStringAsync(), await results[1].Content.ReadAsStringAsync());
        s.Factory.WithDb(db => Assert.Equal(3, db.Contacts.Count(c => c.WorkspaceId == s.Workspace.Workspace.Id)));
    }

    [Fact]
    public async Task S10_CompetingProcessorsCommitOnePublication()
    {
        using var s = new Scenario(); await s.Initialize();
        var collection = await Read<CollectionResponse>(await s.A.PostAsJsonAsync($"/api/workspaces/{s.Workspace.Workspace.Id}/collections", new CreateCollectionRequest("Guides"), Json));
        var article = await Read<ArticleResponse>(await s.A.PostAsJsonAsync($"/api/workspaces/{s.Workspace.Workspace.Id}/articles", new CreateArticleRequest(collection.Id, "Scheduled", "Approved"), Json));
        var at = DateTimeOffset.UtcNow.AddMinutes(1);
        (await s.A.PutAsJsonAsync($"/api/articles/{article.Id}/publication-schedule", new SetPublicationScheduleRequest(article.Version, at), Json)).EnsureSuccessStatusCode();
        using var firstScope = s.Factory.Services.CreateScope(); using var secondScope = s.Factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<PublicationScheduler>();
        var second = secondScope.ServiceProvider.GetRequiredService<PublicationScheduler>();
        var counts = await Task.WhenAll(first.ProcessDueAsync(at), second.ProcessDueAsync(at));
        Assert.Equal(1, counts.Sum());
        s.Factory.WithDb(db => Assert.Single(db.ArticlePublicationSnapshots.Where(p => p.ArticleId == article.Id)));
    }

    private static async Task<HttpResponseMessage[]> Race(Func<Task<HttpResponseMessage>> a, Func<Task<HttpResponseMessage>> b)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () => { await gate.Task; return await a(); });
        var second = Task.Run(async () => { await gate.Task; return await b(); });
        gate.SetResult(); return await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task S18_TwoClaimsForOneRemainingSlotCannotOverfillTheCaller()
    {
        using var s = new Scenario(); await s.Initialize();
        await Read<ConversationDetailResponse>(await s.Start(s.A)); await Read<ConversationDetailResponse>(await s.Start(s.A));
        s.Factory.WithDb(db => { db.Teammates.Single(t => t.Id == s.Workspace.Admin.Id).CapacityLimit = 1; db.SaveChanges(); });
        var route = $"/api/workspaces/{s.Workspace.Workspace.Id}/conversations/claim-next";
        var results = await Race(() => s.A.PostAsJsonAsync(route, new ClaimNextRequest(), Json), () => s.B.PostAsJsonAsync(route, new ClaimNextRequest(), Json));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.ServiceUnavailable }));
        s.Factory.WithDb(db => Assert.Single(db.Conversations.Where(c => c.AssignedTeammateId == s.Workspace.Admin.Id)));
    }

    [Fact]
    public async Task S18_TwoTeammatesCannotBothClaimTheOnlyCandidate()
    {
        using var s = new Scenario(); await s.Initialize(); await Read<ConversationDetailResponse>(await s.Start(s.A));
        var agent = await Read<TeammateCreatedResponse>(await s.A.PostAsJsonAsync($"/api/workspaces/{s.Workspace.Workspace.Id}/teammates", new CreateTeammateRequest("B", "b@race.test", TeammateRole.Agent), Json));
        s.B.DefaultRequestHeaders.Remove("X-Api-Key"); s.B.DefaultRequestHeaders.Add("X-Api-Key", agent.ApiKey);
        var route = $"/api/workspaces/{s.Workspace.Workspace.Id}/conversations/claim-next";
        var results = await Race(() => s.A.PostAsJsonAsync(route, new ClaimNextRequest(), Json), () => s.B.PostAsJsonAsync(route, new ClaimNextRequest(), Json));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.ServiceUnavailable }));
        s.Factory.WithDb(db => Assert.Single(db.AssignmentEvents));
    }

    [Fact]
    public async Task S19_CloseVersusRequiredItemCannotCommitAnInvalidClosedThread()
    {
        using var s = new Scenario(); await s.Initialize(); var conversation = await Read<ConversationDetailResponse>(await s.Start(s.A));
        var results = await Race(
            () => s.A.PostAsJsonAsync($"/api/conversations/{conversation.Id}/state", new ChangeStateRequest(ConversationState.Closed, null), Json),
            () => s.B.PostAsJsonAsync($"/api/conversations/{conversation.Id}/checklist-items", new CreateChecklistItemRequest("Required", true), Json));
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity, HttpStatusCode.ServiceUnavailable }));
        s.Factory.WithDb(db =>
        {
            var parent = db.Conversations.Single(c => c.Id == conversation.Id);
            Assert.False(parent.State == ConversationState.Closed && db.ConversationChecklistItems.Any(i => i.ConversationId == parent.Id && i.Required && i.CompletedAt == null));
        });
    }

    [Fact]
    public async Task S20_ArchiveAndCreationHaveOneDurableOrder()
    {
        using var s = new Scenario(); await s.Initialize();
        var results = await Race(() => s.A.PutAsJsonAsync($"/api/inboxes/{s.Inbox.Id}/archive", new SetInboxArchivedRequest(true), Json), () => s.Start(s.B));
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity, HttpStatusCode.ServiceUnavailable }));
        (await s.A.PutAsJsonAsync($"/api/inboxes/{s.Inbox.Id}/archive", new SetInboxArchivedRequest(true), Json)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await s.Start(s.B)).StatusCode);
        s.Factory.WithDb(db =>
        {
            var archivedAt = db.Inboxes.Single(i => i.Id == s.Inbox.Id).ArchivedAt!.Value;
            Assert.All(db.Conversations.Where(c => c.InboxId == s.Inbox.Id), c => Assert.True(c.CreatedAt <= archivedAt));
        });
    }
}
