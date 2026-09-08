using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class ContactWorkflowRollbackTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task S06_FailedSourceDeleteRollsBackEarlierReferenceUpdates()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var source = await CreateContactAsync(ws.Id, "Source", "same@example.test");
        var target = await CreateContactAsync(ws.Id, "Target", "same@example.test");
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, source.Id);
        var plan = await ReadAsync<ContactMergePlanResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/contact-merges/preview",
            new PreviewContactMergeRequest(source.Id, target.Id), Json));
        var interrupted = false;
        Factory.FailCommand = command =>
        {
            if (!command.CommandText.Contains("DELETE FROM \"Contacts\"")) return false;
            interrupted = true; return true;
        };
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsync($"/api/contact-merges/{plan.Id}/commit", null)).StatusCode); }
        finally { Factory.FailCommand = null; }
        Assert.True(interrupted);
        Factory.WithDb(db =>
        {
            Assert.Equal(2, db.Contacts.Count(c => c.WorkspaceId == ws.Id));
            Assert.Equal(source.Id, db.Conversations.Single(c => c.Id == conversation.Id).ContactId);
            Assert.All(db.Messages.Where(m => m.ConversationId == conversation.Id), m => Assert.Equal(source.Id, m.AuthorContactId));
            Assert.Null(db.ContactMergePlans.Single(p => p.Id == plan.Id).CommittedAt);
        });
    }

    [Fact]
    public async Task S07_FailedSecondContactInsertLeavesReadyBatchAndNoLiveRows()
    {
        var ws = await CreateWorkspaceAsync();
        var batch = await ReadAsync<ContactImportResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/contact-imports",
            new { rows = new[] { new { name = "Ada" }, new { name = "Sam" } } }, Json));
        var inserts = 0;
        Factory.FailCommand = command => command.CommandText.Contains("INSERT INTO \"Contacts\"") && ++inserts == 2;
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null)).StatusCode); }
        finally { Factory.FailCommand = null; }
        Assert.Equal(2, inserts);
        Factory.WithDb(db =>
        {
            Assert.Empty(db.Contacts.Where(c => c.WorkspaceId == ws.Id));
            Assert.Equal(ContactImportStatus.Ready, db.ContactImportBatches.Single(b => b.Id == batch.Id).Status);
            Assert.All(db.ContactImportRows.Where(r => r.BatchId == batch.Id), r => Assert.Null(r.ContactId));
        });
        Assert.Equal(ContactImportStatus.Committed, (await ReadAsync<ContactImportResponse>(await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null))).Status);
    }

    [Fact]
    public async Task S08_NewEmailContactIntermediateSaveIsRolledBackWithFailedConversation()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await ReadAsync<InboxResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes",
            new CreateInboxRequest("Mail", null, false, "support@example.test"), Json));
        var team = await ReadAsync<TeamResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/teams", new CreateTeamRequest("Specialists"), Json));
        var rule = new RoutingRule(Guid.NewGuid(), "Has email", team.Id, new SegmentRuleSet(SegmentMatch.All,
            [new SegmentCondition("email", SegmentOperator.Exists)]));
        await ReadAsync<RoutingRulesResponse>(await Client.PutAsJsonAsync($"/api/inboxes/{inbox.Id}/routing-rules", new SetRoutingRulesRequest(null, [rule]), Json));
        var contactWasInserted = false;
        Factory.FailCommand = command =>
        {
            if (command.CommandText.Contains("INSERT INTO \"Contacts\"")) contactWasInserted = true;
            return command.CommandText.Contains("INSERT INTO \"Conversations\"");
        };
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound",
                new InboundEmailRequest("new@example.test", "New", "support@example.test", "Help", "Hello", "<rollback-routing>"), Json)).StatusCode);
        }
        finally { Factory.FailCommand = null; }
        Assert.True(contactWasInserted);
        Factory.WithDb(db =>
        {
            Assert.Empty(db.Contacts.Where(c => c.WorkspaceId == ws.Id));
            Assert.Empty(db.Conversations.Where(c => c.WorkspaceId == ws.Id));
            Assert.Empty(db.InboundEmailReceipts.Where(r => r.WorkspaceId == ws.Id));
        });
    }

    [Fact]
    public async Task S09_FailedNotificationInsertRollsBackItsMessageAndActivity()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync($"/api/conversations/{conversation.Id}/watch", null)).StatusCode);
        Guid version = default;
        Factory.WithDb(db => version = db.Conversations.Single(c => c.Id == conversation.Id).Version);
        var interrupted = false;
        Factory.FailCommand = command =>
        {
            if (!command.CommandText.Contains("INSERT INTO \"ReplyNotifications\"")) return false;
            interrupted = true; return true;
        };
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
                new AddMessageRequest(AuthorType.Contact, contact.Id, MessageKind.Reply, "Must roll back"), Json)).StatusCode);
        }
        finally { Factory.FailCommand = null; }
        Assert.True(interrupted);
        Factory.WithDb(db =>
        {
            Assert.Single(db.Messages.Where(m => m.ConversationId == conversation.Id));
            Assert.Empty(db.ReplyNotifications.Where(n => n.ConversationId == conversation.Id));
            Assert.Equal(version, db.Conversations.Single(c => c.Id == conversation.Id).Version);
        });
    }
}
