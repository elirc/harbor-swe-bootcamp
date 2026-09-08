using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class CannedReplyLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData(MessageChannel.Chat)]
    [InlineData(MessageChannel.Email)]
    public async Task M26_M27_PreviewHasNoEffectsAndSendUsesTheRealReplyWorkflow(MessageChannel channel)
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id, slaMinutes: 30);
        var contact = await CreateContactAsync(ws.Id, "Ada");
        var agent = await CreateTeammateAsync(ws.Id, "Sam");
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id, "Billing");
        BackdateConversation(conversation.Id, TimeSpan.FromHours(1));
        Factory.WithDb(db => { db.Conversations.Single(c => c.Id == conversation.Id).Channel = channel; db.SaveChanges(); });
        var subscription = await ReadAsync<WebhookCreatedResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/webhooks",
            new CreateWebhookRequest("https://example.test/canned", [WebhookEventType.MessageCreated]), Json));
        var template = await ReadAsync<CannedReplyResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/canned-replies",
            new CreateCannedReplyRequest("check", "Checking", "Hi {{contact.name}}, {{teammate.name}} is checking {{conversation.subject}}."), Json));
        var before = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        ActAs(agent.ApiKey);
        var route = $"/api/conversations/{conversation.Id}/canned-replies/{template.Id}";
        var preview = await ReadAsync<CannedReplyRenderResult>(await Client.PostAsync(route + "/preview", null));
        Assert.Equal("Hi Ada, Sam is checking Billing.", preview.Body);
        Factory.WithDb(db =>
        {
            Assert.Single(db.Messages.Where(m => m.ConversationId == conversation.Id));
            Assert.Empty(db.WebhookDeliveries.Where(d => d.SubscriptionId == subscription.Id));
            Assert.Equal(before.UpdatedAt, db.Conversations.Single(c => c.Id == conversation.Id).UpdatedAt);
        });
        var sent = await ReadAsync<MessageResponse>(await Client.PostAsync(route + "/send", null));
        Assert.Equal(preview.Body, sent.Body);
        Assert.Equal(agent.Id, sent.AuthorTeammateId);
        Assert.Equal(AuthorType.Teammate, sent.AuthorType);
        Assert.Equal(MessageKind.Reply, sent.Kind);
        Assert.Equal(channel, sent.Channel);
        Assert.Equal(channel == MessageChannel.Email, sent.EmailMessageId is not null);
        Factory.WithDb(db =>
        {
            Assert.Equal(sent.CreatedAt, db.Conversations.Single(c => c.Id == conversation.Id).FirstRespondedAt);
            Assert.Single(db.WebhookDeliveries.Where(d => d.SubscriptionId == subscription.Id));
            Assert.Single(db.SlaBreachEvents.Where(e => e.ConversationId == conversation.Id));
        });
        var second = await ReadAsync<MessageResponse>(await Client.PostAsync(route + "/send", null));
        Assert.NotEqual(sent.Id, second.Id);
        Factory.WithDb(db => Assert.Equal(sent.CreatedAt, db.Conversations.Single(c => c.Id == conversation.Id).FirstRespondedAt));
    }

    [Fact]
    public async Task M26_M27_InvalidOrBlankRenderingCreatesNoMessage()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var blank = await ReadAsync<CannedReplyResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/canned-replies",
            new CreateCannedReplyRequest("blank", "Blank", "{{contact.email}}"), Json));
        var bad = await ReadAsync<CannedReplyResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/canned-replies",
            new CreateCannedReplyRequest("bad", "Bad", "{{unknown}}"), Json));
        foreach (var replyId in new[] { blank.Id, bad.Id, Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsync(
                $"/api/conversations/{conversation.Id}/canned-replies/{replyId}/send", null)).StatusCode);
        Factory.WithDb(db =>
        {
            Assert.Single(db.Messages.Where(m => m.ConversationId == conversation.Id));
            Assert.Null(db.Conversations.Single(c => c.Id == conversation.Id).FirstRespondedAt);
            Assert.Equal(conversation.UpdatedAt, db.Conversations.Single(c => c.Id == conversation.Id).UpdatedAt);
        });
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync(
            $"/api/conversations/{conversation.Id}/canned-replies/{blank.Id}/preview", null)).StatusCode);
    }
}
