using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class S18S19S20WorkflowTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task ClaimUsesPriorityAndCurrentCapacityWithoutChangingActivityOrPointer()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var normal = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var urgent = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        (await Client.PutAsJsonAsync($"/api/conversations/{urgent.Id}/priority", new SetPriorityRequest(ConversationPriority.Urgent), Json)).EnsureSuccessStatusCode();
        Guid owner = default;
        Factory.WithDb(db => { var admin = db.Teammates.Single(t => t.WorkspaceId == ws.Id); owner = admin.Id; admin.CapacityLimit = 1; db.SaveChanges(); });
        var claimed = await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/conversations/claim-next", new ClaimNextRequest(), Json));
        Assert.Equal(urgent.Id, claimed.Id); Assert.Equal(owner, claimed.AssignedTeammateId);
        Assert.Equal(urgent.LastMessageAt, claimed.LastMessageAt);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/conversations/claim-next", new ClaimNextRequest(), Json)).StatusCode);
        Factory.WithDb(db =>
        {
            Assert.Null(db.Inboxes.Single(i => i.Id == inbox.Id).LastAssignedTeammateId);
            Assert.Null(db.Conversations.Single(c => c.Id == normal.Id).AssignedTeammateId);
            Assert.Single(db.AssignmentEvents.Where(e => e.ConversationId == urgent.Id));
        });
    }

    [Fact]
    public async Task RequiredChecklistBlocksCloseAndChangesParentVersionEvenAtSameTime()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        Guid before = default;
        Factory.WithDb(db => before = db.Conversations.Single(c => c.Id == conversation.Id).Version);
        Factory.FrozenTime = conversation.UpdatedAt;
        ConversationChecklistItem required;
        try
        {
            required = await ReadAsync<ConversationChecklistItem>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/checklist-items", new CreateChecklistItemRequest("  Verify refund  ", true), Json));
            Assert.Equal("Verify refund", required.Title);
            Factory.WithDb(db => { var current = db.Conversations.Single(c => c.Id == conversation.Id); Assert.NotEqual(before, current.Version); Assert.Equal(conversation.LastMessageAt, current.LastMessageAt); });
        }
        finally { Factory.FrozenTime = null; }
        await ReadAsync<ConversationChecklistItem>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/checklist-items", new CreateChecklistItemRequest("Optional survey", false), Json));
        var close = await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/state", new ChangeStateRequest(ConversationState.Closed, null), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, close.StatusCode);
        Assert.Contains(required.Id.ToString(), await close.Content.ReadAsStringAsync());
        var completed = await ReadAsync<ConversationChecklistItem>(await Client.PutAsJsonAsync($"/api/checklist-items/{required.Id}/completion", new CompleteChecklistItemRequest(true, required.Version), Json));
        Assert.NotNull(completed.CompletedAt); Assert.NotEqual(required.Version, completed.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/checklist-items/{required.Id}/completion", new CompleteChecklistItemRequest(false, required.Version), Json)).StatusCode);
        (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/state", new ChangeStateRequest(ConversationState.Closed, null), Json)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/checklist-items/{required.Id}/completion", new CompleteChecklistItemRequest(false, completed.Version), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/checklist-items", new CreateChecklistItemRequest("Late item", true), Json)).StatusCode);
    }

    [Fact]
    public async Task ArchiveBlocksNewWorkButKeepsValidatedRepliesReplayAndHistory()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await ReadAsync<InboxResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes", new CreateInboxRequest("Old support", 60, false, "old@archive.test"), Json));
        var initial = new InboundEmailRequest("ada@archive.test", "Ada", "old@archive.test", "Help", "First", "<archive-first>");
        var mail = await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound", initial, Json));
        var archived = await ReadAsync<InboxResponse>(await Client.PutAsJsonAsync($"/api/inboxes/{inbox.Id}/archive", new SetInboxArchivedRequest(true), Json));
        Assert.NotNull(archived.ArchivedAt);
        var repeat = await ReadAsync<InboxResponse>(await Client.PutAsJsonAsync($"/api/inboxes/{inbox.Id}/archive", new SetInboxArchivedRequest(true), Json));
        Assert.Equal(archived.ArchivedAt, repeat.ArchivedAt);
        Assert.Empty(await ReadAsync<List<InboxResponse>>(await Client.GetAsync($"/api/workspaces/{ws.Id}/inboxes")));
        Assert.Single(await ReadAsync<List<InboxResponse>>(await Client.GetAsync($"/api/workspaces/{ws.Id}/inboxes?includeArchived=true")));
        Assert.Equal(mail, await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound", initial, Json)));
        var reply = initial with { Body = "Reply", MessageId = "<archive-reply>", InReplyTo = "<archive-first>" };
        Assert.False((await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound", reply, Json))).StartedNewConversation);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound", initial with { From = "new@archive.test", MessageId = "<new>" }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/conversations", new StartConversationRequest(inbox.Id, mail.ContactId, "New", "Hello"), Json)).StatusCode);
        Factory.WithDb(db => { Assert.Single(db.Contacts.Where(c => c.WorkspaceId == ws.Id)); Assert.Single(db.Conversations.Where(c => c.WorkspaceId == ws.Id)); });
        var restored = await ReadAsync<InboxResponse>(await Client.PutAsJsonAsync($"/api/inboxes/{inbox.Id}/archive", new SetInboxArchivedRequest(false), Json));
        Assert.Null(restored.ArchivedAt);
        (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/conversations", new StartConversationRequest(inbox.Id, mail.ContactId, "Restored", "Hello"), Json)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task WatchAndChecklistLimitsRejectOnlyNewAdditions()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var target = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var outsider = await CreateTeammateAsync(ws.Id, "Watcher 201");
        Factory.WithDb(db =>
        {
            var owner = db.Teammates.Single(t => t.WorkspaceId == ws.Id && t.Role == TeammateRole.Admin).Id;
            var people = Enumerable.Range(0, 199).Select(i => new Teammate { WorkspaceId = ws.Id, Name = "Watcher " + i,
                Email = $"watcher-{i}@limits.test", ApiKeyHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") }).ToList();
            db.Teammates.AddRange(people);
            db.ConversationWatchers.AddRange(people.Select(p => p.Id).Append(owner).Select(person => new ConversationWatcher
                { WorkspaceId = ws.Id, ConversationId = target.Id, TeammateId = person }));
            db.ConversationChecklistItems.AddRange(Enumerable.Range(0, 50).Select(i => new ConversationChecklistItem { WorkspaceId = ws.Id, ConversationId = target.Id, Title = "Item " + i }));
            db.SaveChanges();
        });
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync($"/api/conversations/{target.Id}/watch", null)).StatusCode);
        ActAs(outsider.ApiKey);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PutAsync($"/api/conversations/{target.Id}/watch", null)).StatusCode);
        ActAsAdminOf(ws.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/conversations/{target.Id}/checklist-items", new CreateChecklistItemRequest("Too many", true), Json)).StatusCode);
    }
}
