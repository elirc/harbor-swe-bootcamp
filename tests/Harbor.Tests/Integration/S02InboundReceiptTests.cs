using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;

namespace Harbor.Tests.Integration;

public class S02InboundReceiptTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<InboxResponse> Inbox(Guid ws, string address = "support@example.test") =>
        await ReadAsync<InboxResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws}/inboxes",
            new CreateInboxRequest("Mail", 60, false, address), Json));
    private Task<HttpResponseMessage> Send(Guid ws, InboundEmailRequest request) =>
        Client.PostAsJsonAsync($"/api/workspaces/{ws}/email/inbound", request, Json);

    [Fact]
    public async Task NewAndReplyReplaysAreInertAndRetainProviderIdentity()
    {
        var ws = await CreateWorkspaceAsync();
        await Inbox(ws.Id);
        var input = new InboundEmailRequest("ada@example.test", "Ada", "support@example.test", "Help", "Opening", " <Mail-1> ");
        var first = await Send(ws.Id, input);
        var original = await ReadAsync<InboundEmailResponse>(first);
        var replay = await Send(ws.Id, input with { MessageId = "<Mail-1>", From = "ADA@example.test" });
        Assert.Equal(original, await ReadAsync<InboundEmailResponse>(replay));
        Assert.Equal("true", replay.Headers.GetValues("Inbound-Replayed").Single());
        Assert.Equal(first.Headers.GetValues("Inbound-Receipt-Id"), replay.Headers.GetValues("Inbound-Receipt-Id"));
        var replyInput = input with { Body = "Follow up", MessageId = "<Mail-2>", InReplyTo = "<Mail-1>" };
        var reply = await ReadAsync<InboundEmailResponse>(await Send(ws.Id, replyInput));
        Assert.False(reply.StartedNewConversation);
        DateTimeOffset? seen = null;
        Factory.WithDb(db => seen = db.Contacts.Single(c => c.Id == original.ContactId).LastSeenAt);
        Assert.Equal(reply, await ReadAsync<InboundEmailResponse>(await Send(ws.Id, replyInput)));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(ws.Id, replyInput with { Body = "Changed" })).StatusCode);
        Factory.WithDb(db =>
        {
            Assert.Equal(seen, db.Contacts.Single(c => c.Id == original.ContactId).LastSeenAt);
            Assert.Equal(2, db.Messages.Count(m => m.ConversationId == original.ConversationId));
            Assert.Equal("<Mail-2>", db.Messages.Single(m => m.Id == reply.MessageId).EmailMessageId);
            Assert.Equal(2, db.InboundEmailReceipts.Count(r => r.WorkspaceId == ws.Id));
        });
        var receiptId = first.Headers.GetValues("Inbound-Receipt-Id").Single();
        var metadata = await Client.GetAsync($"/api/inbound-email-receipts/{receiptId}");
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        Assert.DoesNotContain("Opening", await metadata.Content.ReadAsStringAsync());
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/inbound-email-receipts/{receiptId}")).StatusCode);
    }

    [Fact]
    public async Task WrongSenderOrInboxIsRejectedBeforeCreatingAContact()
    {
        var ws = await CreateWorkspaceAsync();
        await Inbox(ws.Id);
        await Inbox(ws.Id, "billing@example.test");
        var input = new InboundEmailRequest("ada@example.test", "Ada", "support@example.test", "Help", "Opening", "<first>");
        var original = await ReadAsync<InboundEmailResponse>(await Send(ws.Id, input));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Send(ws.Id, input with { From = "intruder@example.test", MessageId = "<bad>", InReplyTo = "<first>" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Send(ws.Id, input with { To = "billing@example.test", MessageId = "<bad>", InReplyTo = "<first>" })).StatusCode);
        Factory.WithDb(db =>
        {
            Assert.Single(db.Contacts.Where(c => c.WorkspaceId == ws.Id));
            Assert.Single(db.Messages.Where(m => m.ConversationId == original.ConversationId));
            Assert.Single(db.InboundEmailReceipts.Where(r => r.WorkspaceId == ws.Id));
        });
        var independent = await ReadAsync<InboundEmailResponse>(await Send(ws.Id, input with { To = "billing@example.test" }));
        Assert.NotEqual(original.ConversationId, independent.ConversationId);
        var caseDifferent = await ReadAsync<InboundEmailResponse>(await Send(ws.Id, input with { MessageId = "<FIRST>" }));
        Assert.NotEqual(original.MessageId, caseDifferent.MessageId);
        var blank = input with { MessageId = " " };
        var one = await ReadAsync<InboundEmailResponse>(await Send(ws.Id, blank));
        var two = await ReadAsync<InboundEmailResponse>(await Send(ws.Id, blank));
        Assert.NotEqual(one.MessageId, two.MessageId);
    }
}
