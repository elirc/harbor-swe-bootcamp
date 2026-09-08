using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class IngressConcurrencyTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task S02_ProviderRetriesRaceWithoutOrphanContactsOrDuplicateMessages()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harbor-mail-race-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new HarborApiFactory { FileDatabasePath = path };
            using var client = factory.CreateClient();
            var bootstrap = await Read<CreateWorkspaceResponse>(await client.PostAsJsonAsync("/api/workspaces",
                new CreateWorkspaceRequest("Mail", "Owner", "owner@example.test"), Json));
            client.DefaultRequestHeaders.Add("X-Api-Key", bootstrap.ApiKey);
            var ws = bootstrap.Workspace.Id;
            await Read<InboxResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws}/inboxes",
                new CreateInboxRequest("Support", null, false, "support@example.test"), Json));
            using var other = factory.CreateClient();
            other.DefaultRequestHeaders.Add("X-Api-Key", bootstrap.ApiKey);
            var route = $"/api/workspaces/{ws}/email/inbound";
            var body = new InboundEmailRequest("ada@example.test", "Ada", "support@example.test", "Question", "Hello", "<same-provider-id>");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpResponseMessage> Send(HttpClient caller) { await gate.Task; return await caller.PostAsJsonAsync(route, body, Json); }
            var first = Task.Run(() => Send(client));
            var second = Task.Run(() => Send(other));
            gate.SetResult();
            var responses = await Task.WhenAll(first, second);
            for (var i = 0; i < responses.Length; i++)
                if (responses[i].StatusCode == HttpStatusCode.ServiceUnavailable)
                    responses[i] = await client.PostAsJsonAsync(route, body, Json);
            Assert.Equal(await Read<InboundEmailResponse>(responses[0]), await Read<InboundEmailResponse>(responses[1]));
            factory.WithDb(db =>
            {
                Assert.Single(db.Contacts.Where(c => c.WorkspaceId == ws));
                Assert.Single(db.Conversations.Where(c => c.WorkspaceId == ws));
                Assert.Single(db.Messages.Where(m => m.Conversation!.WorkspaceId == ws));
                Assert.Single(db.InboundEmailReceipts.Where(r => r.WorkspaceId == ws));
            });
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task S01_SameKeyRacingHttpCallsCommitOneCreationAndOutbox()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harbor-http-race-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new HarborApiFactory { FileDatabasePath = path };
            using var client = factory.CreateClient();
            var bootstrap = await Read<CreateWorkspaceResponse>(await client.PostAsJsonAsync("/api/workspaces",
                new CreateWorkspaceRequest("Race", "Owner", "owner@example.test"), Json));
            client.DefaultRequestHeaders.Add("X-Api-Key", bootstrap.ApiKey);
            var ws = bootstrap.Workspace.Id;
            var inbox = await Read<InboxResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws}/inboxes", new CreateInboxRequest("Support", null), Json));
            var contact = await Read<ContactResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws}/contacts", new CreateContactRequest("Ada", null, null), Json));
            await Read<WebhookCreatedResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws}/webhooks",
                new CreateWebhookRequest("https://example.test/events", [WebhookEventType.ConversationCreated]), Json));
            using var other = factory.CreateClient();
            other.DefaultRequestHeaders.Add("X-Api-Key", bootstrap.ApiKey);
            client.DefaultRequestHeaders.Add("Idempotency-Key", "same-request");
            other.DefaultRequestHeaders.Add("Idempotency-Key", "same-request");
            var body = new StartConversationRequest(inbox.Id, contact.Id, "Question", "Hello");
            var route = $"/api/workspaces/{ws}/conversations";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpResponseMessage> Send(HttpClient caller) { await gate.Task; return await caller.PostAsJsonAsync(route, body, Json); }
            var first = Task.Run(() => Send(client));
            var second = Task.Run(() => Send(other));
            gate.SetResult();
            var responses = await Task.WhenAll(first, second);
            for (var i = 0; i < responses.Length; i++)
                if (responses[i].StatusCode == HttpStatusCode.ServiceUnavailable)
                    responses[i] = await client.PostAsJsonAsync(route, body, Json);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
            factory.WithDb(db =>
            {
                Assert.Single(db.Conversations.Where(c => c.WorkspaceId == ws));
                Assert.Single(db.Messages.Where(m => m.Conversation!.WorkspaceId == ws));
                Assert.Single(db.CreationReceipts.Where(r => r.WorkspaceId == ws));
                Assert.Single(db.WebhookDeliveries.Where(d => d.WorkspaceId == ws));
            });
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task S05_ConcurrentIssuanceCannotExceedFiveUsableCredentials()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harbor-key-race-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new HarborApiFactory { FileDatabasePath = path };
            using var client = factory.CreateClient();
            var bootstrap = await Read<CreateWorkspaceResponse>(await client.PostAsJsonAsync("/api/workspaces",
                new CreateWorkspaceRequest("Keys", "Owner", "owner@example.test"), Json));
            client.DefaultRequestHeaders.Add("X-Api-Key", bootstrap.ApiKey);
            var route = $"/api/teammates/{bootstrap.Admin.Id}/api-keys";
            for (var i = 0; i < 3; i++) await Read<CreatedApiKeyResponse>(await client.PostAsJsonAsync(route, new CreateApiKeyRequest("Existing " + i, 60), Json));
            using var other = factory.CreateClient();
            other.DefaultRequestHeaders.Add("X-Api-Key", bootstrap.ApiKey);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpResponseMessage> Send(HttpClient caller) { await gate.Task; return await caller.PostAsJsonAsync(route, new CreateApiKeyRequest("Contested", 60), Json); }
            var first = Task.Run(() => Send(client));
            var second = Task.Run(() => Send(other));
            gate.SetResult();
            var results = await Task.WhenAll(first, second);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r =>
                Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, new CreateApiKeyRequest("Too many", 60), Json)).StatusCode);
            factory.WithDb(db => Assert.Equal(5, db.TeammateApiKeys.Count(k => k.TeammateId == bootstrap.Admin.Id && k.RevokedAt == null)));
        }
        finally { Cleanup(path); }
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
}
