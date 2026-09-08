using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class S01CreationReplayTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private Task<HttpResponseMessage> Send(Guid workspace, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{workspace}/conversations")
            { Content = JsonContent.Create(body, options: Json) };
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return Client.SendAsync(request);
    }

    [Fact]
    public async Task RetriedCanonicalRequestReturnsOriginalBodyWithoutEffects()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id, autoAssign: true);
        var contact = await CreateContactAsync(ws.Id);
        await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/webhooks",
            new CreateWebhookRequest("https://example.test/events", [WebhookEventType.ConversationCreated]), Json);
        var body = new { inboxId = inbox.Id, contactId = contact.Id, subject = "Question", body = "Hello" };
        var first = await Send(ws.Id, body, "request_1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var original = await first.Content.ReadAsStringAsync();
        var replay = await Send(ws.Id, new { body = "Hello", priority = "Normal", subject = "Question", contactId = contact.Id, inboxId = inbox.Id }, "request_1");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(ws.Id, body with { body = "Different" }, "request_1")).StatusCode);
        Factory.WithDb(db =>
        {
            Assert.Single(db.Conversations.Where(c => c.WorkspaceId == ws.Id));
            Assert.Single(db.Messages.Where(m => m.Conversation!.WorkspaceId == ws.Id));
            Assert.Single(db.WebhookDeliveries.Where(d => d.WorkspaceId == ws.Id));
            Assert.Single(db.CreationReceipts.Where(r => r.WorkspaceId == ws.Id));
        });
        // Authentication is still performed even though the key has a retained result.
        ActAs("invalid-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(ws.Id, body, "request_1")).StatusCode);
    }

    [Theory]
    [InlineData("space key")]
    [InlineData("é")]
    [InlineData("colon:key")]
    public async Task BadKeyDoesNotCreateRows(string key)
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ws.Id,
            new { inboxId = inbox.Id, contactId = contact.Id, body = "Hello" }, key)).StatusCode);
        Factory.WithDb(db => Assert.Empty(db.CreationReceipts.Where(r => r.WorkspaceId == ws.Id)));
    }

    [Fact]
    public async Task AnActuallyPresentEmptyHeaderIsRejected()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { inboxId = inbox.Id, contactId = contact.Id, body = "Hello" }, Json);
        // HttpClient omits its zero-valued header. Set the server request explicitly
        // to distinguish a present empty header from the legitimate unkeyed route.
        var context = await Factory.Server.SendAsync(context =>
        {
            context.Request.Method = "POST";
            context.Request.Path = $"/api/workspaces/{ws.Id}/conversations";
            context.Request.Headers["X-Api-Key"] = AdminApiKeys[ws.Id];
            context.Request.Headers["Idempotency-Key"] = new Microsoft.Extensions.Primitives.StringValues("");
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
        });
        Assert.Equal(400, context.Response.StatusCode);
        Factory.WithDb(db => Assert.Empty(db.Conversations.Where(c => c.WorkspaceId == ws.Id)));
    }

    [Fact]
    public async Task FailureDoesNotConsumeKeyAndUnkeyedCallsRemainIndependent()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var body = new { inboxId = inbox.Id, contactId = contact.Id, body = "Hello" };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(ws.Id, body with { inboxId = Guid.NewGuid() }, "recover")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(ws.Id, body, "recover")).StatusCode);
        var second = await ReadAsync<ConversationDetailResponse>(await Send(ws.Id, body, null));
        var third = await ReadAsync<ConversationDetailResponse>(await Send(ws.Id, body, null));
        Assert.NotEqual(second.Id, third.Id);
        Factory.WithDb(db => Assert.Single(db.CreationReceipts.Where(r => r.WorkspaceId == ws.Id)));
    }
}
