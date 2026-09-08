using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class S06ContactMergeTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private Task<HttpResponseMessage> Preview(Guid ws, Guid source, Guid target) => Client.PostAsJsonAsync(
        $"/api/workspaces/{ws}/contact-merges/preview", new PreviewContactMergeRequest(source, target), Json);

    [Fact]
    public async Task ReviewedMergePreservesHistoryAndRepeatedCommitReturnsOriginalResult()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var source = await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/contacts",
            new CreateContactRequest("Source", "ada@example.test", "E-1", new Dictionary<string, string?> { ["plan"] = "free", ["seats"] = "10" }), Json));
        var target = await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/contacts",
            new CreateContactRequest("Target", "ADA@example.test", null, new Dictionary<string, string?> { ["plan"] = "pro" }), Json));
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, source.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var note = await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Note, "Keep this note"), Json));
        var before = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        var plan = await ReadAsync<ContactMergePlanResponse>(await Preview(ws.Id, source.Id, target.Id));
        Assert.Equal(1, plan.Preview.ConversationCount);
        Assert.Equal(1, plan.Preview.AuthoredMessageCount);
        Assert.Equal("plan", Assert.Single(plan.Preview.AttributeConflicts).Key);
        Assert.Equal("Target", plan.Preview.TargetFields.Name);
        var otherAdmin = await CreateTeammateAsync(ws.Id, "Reviewer", role: TeammateRole.Admin);
        ActAs(otherAdmin.ApiKey);
        var result = await ReadAsync<ContactMergeResult>(await Client.PostAsync($"/api/contact-merges/{plan.Id}/commit", null));
        Assert.Equal(result, await ReadAsync<ContactMergeResult>(await Client.PostAsync($"/api/contact-merges/{plan.Id}/commit", null)));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/contacts/{source.Id}")).StatusCode);
        var survivor = await ReadAsync<ContactResponse>(await Client.GetAsync($"/api/contacts/{target.Id}"));
        Assert.Equal("Target", survivor.Name);
        Assert.Equal("ADA@example.test", survivor.Email);
        Assert.Equal("E-1", survivor.ExternalId);
        Assert.Equal("pro", survivor.Attributes["plan"]);
        Assert.Equal("10", survivor.Attributes["seats"]);
        var after = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        Assert.Equal(target.Id, after.ContactId);
        Assert.Equal(before.Messages.Select(m => (m.Id, m.Body, m.CreatedAt, m.Kind, m.Channel)),
            after.Messages.Select(m => (m.Id, m.Body, m.CreatedAt, m.Kind, m.Channel)));
        Assert.Equal(agent.Id, after.Messages.Single(m => m.Id == note.Id).AuthorTeammateId);
        Assert.All(after.Messages.Where(m => m.AuthorType == AuthorType.Contact), m => Assert.Equal(target.Id, m.AuthorContactId));
        Factory.WithDb(db => Assert.Equal(otherAdmin.Id, db.ContactMergePlans.Single(p => p.Id == plan.Id).CommittedByTeammateId));
    }

    [Fact]
    public async Task ChangedContactOrIncomingMessageInvalidatesPlanAndExpiryHasAnExactBoundary()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var source = await CreateContactAsync(ws.Id, "Source", "same@example.test");
        var target = await CreateContactAsync(ws.Id, "Target", "same@example.test");
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, source.Id);
        var plan = await ReadAsync<ContactMergePlanResponse>(await Preview(ws.Id, source.Id, target.Id));
        await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Contact, source.Id, MessageKind.Reply, "New evidence"), Json));
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/contact-merges/{plan.Id}/commit", null)).StatusCode);
        var changed = await ReadAsync<ContactMergePlanResponse>(await Preview(ws.Id, source.Id, target.Id));
        Factory.WithDb(db => { db.Contacts.Single(c => c.Id == target.Id).Name = "Changed"; db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/contact-merges/{changed.Id}/commit", null)).StatusCode);
        var expiry = await ReadAsync<ContactMergePlanResponse>(await Preview(ws.Id, source.Id, target.Id));
        Factory.FrozenTime = expiry.ExpiresAt;
        try { Assert.Equal(HttpStatusCode.Gone, (await Client.PostAsync($"/api/contact-merges/{expiry.Id}/commit", null)).StatusCode); }
        finally { Factory.FrozenTime = null; }
        Factory.WithDb(db =>
        {
            Assert.Equal(2, db.Contacts.Count(c => c.WorkspaceId == ws.Id));
            Assert.Equal(source.Id, db.Conversations.Single(c => c.Id == conversation.Id).ContactId);
        });
    }

    [Fact]
    public async Task EligibilityBoundsAndAuthorizationRejectBeforeMovingAnything()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var source = await CreateContactAsync(ws.Id, "Source", "same@example.test");
        var target = await CreateContactAsync(ws.Id, "Target", "other@example.test");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Preview(ws.Id, source.Id, target.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Preview(ws.Id, source.Id, source.Id)).StatusCode);
        Factory.WithDb(db =>
        {
            db.Contacts.Single(c => c.Id == target.Id).Email = source.Email;
            for (var i = 0; i < 101; i++) db.Conversations.Add(new Conversation { WorkspaceId = ws.Id, InboxId = inbox.Id, ContactId = source.Id });
            db.SaveChanges();
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Preview(ws.Id, source.Id, target.Id)).StatusCode);
        var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Preview(ws.Id, source.Id, target.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync($"/api/contact-merges/{Guid.NewGuid()}/commit", null)).StatusCode);
        Factory.WithDb(db => Assert.Empty(db.ContactMergePlans.Where(p => p.WorkspaceId == ws.Id)));
    }
}
