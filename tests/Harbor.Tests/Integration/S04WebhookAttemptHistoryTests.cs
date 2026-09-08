using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class S04WebhookAttemptHistoryTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task HistoryIsPagedScopedAndContainsNoTransportSecrets()
    {
        var ws = await CreateWorkspaceAsync();
        var subscription = await ReadAsync<WebhookCreatedResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/webhooks",
            new CreateWebhookRequest("https://example.test/hooks", [WebhookEventType.MessageCreated]), Json));
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        var id = Guid.NewGuid();
        Factory.WithDb(db =>
        {
            db.WebhookDeliveries.Add(new WebhookDelivery { Id = id, WorkspaceId = ws.Id, SubscriptionId = subscription.Id,
                EventType = WebhookEventType.MessageCreated, Payload = "private-payload", NextAttemptAt = now });
            db.SaveChanges();
        });
        Factory.FrozenTime = now;
        try
        {
            Factory.WebhookSender.Respond = _ => WebhookSendResult.Rejected(500);
            await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync($"/api/workspaces/{ws.Id}/webhooks/dispatch", null));
            Factory.FrozenTime = now.AddMinutes(1);
            Factory.WebhookSender.Respond = _ => WebhookSendResult.Unreachable("private-exception-text");
            await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync($"/api/workspaces/{ws.Id}/webhooks/dispatch", null));
            Factory.FrozenTime = now.AddMinutes(3);
            Factory.WebhookSender.Respond = _ => WebhookSendResult.Ok(204);
            await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync($"/api/workspaces/{ws.Id}/webhooks/dispatch", null));
            var response = await Client.GetAsync($"/api/webhook-deliveries/{id}/attempts?page=2&pageSize=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("3", response.Headers.GetValues("X-Total-Count").Single());
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var attempt = Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal(2, attempt.GetProperty("attemptNumber").GetInt32());
            Assert.Equal("TransportError", attempt.GetProperty("outcome").GetString());
            var all = await (await Client.GetAsync($"/api/webhook-deliveries/{id}/attempts")).Content.ReadAsStringAsync();
            foreach (var secret in new[] { subscription.Secret, subscription.Url, "private-payload", "private-exception-text" })
                Assert.DoesNotContain(secret, all);
            var agent = await CreateTeammateAsync(ws.Id);
            ActAs(agent.ApiKey);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"/api/webhook-deliveries/{id}/attempts")).StatusCode);
            await CreateWorkspaceAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/webhook-deliveries/{id}/attempts")).StatusCode);
        }
        finally { Factory.FrozenTime = null; Factory.WebhookSender.Reset(); }
    }

    [Fact]
    public async Task S03_DispatchCapsReservationsAtFifty()
    {
        var ws = await CreateWorkspaceAsync();
        var subscription = await ReadAsync<WebhookCreatedResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/webhooks",
            new CreateWebhookRequest("https://example.test/hooks", [WebhookEventType.MessageCreated]), Json));
        var now = DateTimeOffset.UtcNow;
        Factory.WithDb(db =>
        {
            for (var i = 0; i < 51; i++) db.WebhookDeliveries.Add(new WebhookDelivery { WorkspaceId = ws.Id,
                SubscriptionId = subscription.Id, EventType = WebhookEventType.MessageCreated, Payload = "{}", NextAttemptAt = now });
            db.SaveChanges();
        });
        var first = await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync($"/api/workspaces/{ws.Id}/webhooks/dispatch?limit=200", null));
        Assert.Equal(50, first.Count);
        var last = await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync($"/api/workspaces/{ws.Id}/webhooks/dispatch?limit=200", null));
        Assert.Single(last);
    }
}
