using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class S09WatchedReplyTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<List<ReplyNotificationResponse>> Notifications(Guid ws, bool unreadOnly = true) =>
        await ReadAsync<List<ReplyNotificationResponse>>(await Client.GetAsync($"/api/workspaces/{ws}/my-notifications?unreadOnly={unreadOnly}"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContactRepliesNotifyEachWatcherPrivatelyAcrossBothChannels(bool email)
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await ReadAsync<InboxResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes",
            new CreateInboxRequest("Support", null, false, email ? "support@example.test" : null), Json));
        Guid conversationId;
        Guid contactId;
        if (email)
        {
            var started = await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound",
                new InboundEmailRequest("ada@example.test", "Ada", "support@example.test", "Help", "Opening", "<watch-open>"), Json));
            conversationId = started.ConversationId; contactId = started.ContactId;
        }
        else
        {
            contactId = (await CreateContactAsync(ws.Id)).Id;
            conversationId = (await StartConversationAsync(ws.Id, inbox.Id, contactId)).Id;
        }
        var a = await CreateTeammateAsync(ws.Id, "A");
        var b = await CreateTeammateAsync(ws.Id, "B");
        Assert.Empty(await Notifications(ws.Id));
        foreach (var key in new[] { a.ApiKey, b.ApiKey })
        {
            ActAs(key);
            Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync($"/api/conversations/{conversationId}/watch", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync($"/api/conversations/{conversationId}/watch", null)).StatusCode);
        }
        ActAsAdminOf(ws.Id);
        Guid messageId;
        if (email)
        {
            var input = new InboundEmailRequest("ada@example.test", "Ada", "support@example.test", "Help", "New reply", "<watch-reply>", "<watch-open>");
            var appended = await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound", input, Json));
            messageId = appended.MessageId;
            await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound", input, Json));
        }
        else messageId = (await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new AddMessageRequest(AuthorType.Contact, contactId, MessageKind.Reply, "New reply"), Json))).Id;
        Assert.Empty(await Notifications(ws.Id));
        ActAs(a.ApiKey);
        var first = Assert.Single(await Notifications(ws.Id));
        Assert.Equal(messageId, first.MessageId);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync($"/api/notifications/{first.Id}/read", null)).StatusCode);
        var readAt = Assert.Single(await Notifications(ws.Id, false)).ReadAt;
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsync($"/api/notifications/{first.Id}/read", null)).StatusCode);
        Assert.Equal(readAt, Assert.Single(await Notifications(ws.Id, false)).ReadAt);
        Assert.Empty(await Notifications(ws.Id));
        ActAs(b.ApiKey);
        Assert.Single(await Notifications(ws.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsync($"/api/notifications/{first.Id}/read", null)).StatusCode);
        await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new AddMessageRequest(AuthorType.Teammate, b.Id, MessageKind.Note, "Private note"), Json));
        await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new AddMessageRequest(AuthorType.Teammate, b.Id, MessageKind.Reply, "Our answer"), Json));
        Assert.Single(await Notifications(ws.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/conversations/{conversationId}/watch")).StatusCode);
        await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversationId}/messages",
            new AddMessageRequest(AuthorType.Contact, contactId, MessageKind.Reply, "Another reply"), Json));
        Assert.Single(await Notifications(ws.Id));
        ActAs(a.ApiKey);
        Assert.Single(await Notifications(ws.Id));
        Assert.Equal(2, (await Notifications(ws.Id, false)).Count);
        var conversation = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversationId}"));
        Assert.Null(conversation.AssignedTeamId);
        Assert.Null(conversation.AssignedTeammateId);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsync($"/api/conversations/{conversationId}/watch", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsync($"/api/notifications/{first.Id}/read", null)).StatusCode);
    }
}
