using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class WebhookLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<WebhookCreatedResponse> Subscription(Guid workspace, string suffix) =>
        await ReadAsync<WebhookCreatedResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{workspace}/webhooks",
            new CreateWebhookRequest("https://example.test/" + suffix, [WebhookEventType.MessageCreated]), Json));

    [Fact]
    public async Task M28_BatchesSkipPausedRowsBeforeTakingSlots()
    {
        var ws = await CreateWorkspaceAsync();
        var active = await Subscription(ws.Id, "active");
        var paused = await Subscription(ws.Id, "paused");
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        Factory.WithDb(db =>
        {
            db.WebhookSubscriptions.Single(s => s.Id == paused.Id).IsActive = false;
            for (var i = 0; i < 201; i++) db.WebhookDeliveries.Add(new WebhookDelivery
            {
                WorkspaceId = ws.Id, SubscriptionId = paused.Id, EventType = WebhookEventType.MessageCreated,
                Payload = "{}", CreatedAt = now.AddDays(-1), NextAttemptAt = now,
            });
            for (var i = 0; i < 5; i++) db.WebhookDeliveries.Add(new WebhookDelivery
            {
                WorkspaceId = ws.Id, SubscriptionId = active.Id, EventType = WebhookEventType.MessageCreated,
                Payload = "{}", CreatedAt = now.AddMinutes(i - 5), NextAttemptAt = now,
            });
            db.SaveChanges();
        });
        Factory.FrozenTime = now;
        Factory.WebhookSender.Reset();
        try
        {
            var route = $"/api/workspaces/{ws.Id}/webhooks/dispatch";
            foreach (var expected in new[] { 2, 2, 1, 0 })
            {
                var rows = await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync(route + "?limit=2", null));
                Assert.Equal(expected, rows.Count);
                Assert.All(rows, row => Assert.Equal(WebhookDeliveryStatus.Succeeded, row.Status));
            }
            Assert.Equal(5, Factory.WebhookSender.Sent.Count);
            Assert.All(Factory.WebhookSender.Sent, sent => Assert.Equal(active.Url, sent.Url));
            Factory.WithDb(db => Assert.All(db.WebhookDeliveries.Where(d => d.SubscriptionId == paused.Id), d => Assert.Equal(0, d.AttemptCount)));
            foreach (var limit in new[] { 0, 201 })
                Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync(route + $"?limit={limit}", null)).StatusCode);
        }
        finally { Factory.FrozenTime = null; Factory.WebhookSender.Reset(); }
    }

    [Fact]
    public async Task M29_RedrivePreservesOriginalAndReturnsOneChildAcrossRetries()
    {
        var ws = await CreateWorkspaceAsync();
        var subscription = await Subscription(ws.Id, "redrive");
        var id = Guid.NewGuid();
        var payload = "{ \"id\": \"" + id + "\", \"text\": \"unchanged\" }";
        Factory.WithDb(db =>
        {
            db.WebhookDeliveries.Add(new WebhookDelivery { Id = id, WorkspaceId = ws.Id, SubscriptionId = subscription.Id,
                EventType = WebhookEventType.MessageCreated, Payload = payload, Status = WebhookDeliveryStatus.Failed,
                AttemptCount = 5, Error = "Exhausted", ResponseStatusCode = 500 });
            db.SaveChanges();
        });
        var route = $"/api/webhook-deliveries/{id}/redrive";
        var created = await Client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var child = await ReadAsync<WebhookDeliveryResponse>(created);
        Assert.NotEqual(id, child.Id);
        Assert.Equal(id, child.OriginalDeliveryId);
        Assert.Equal(payload, child.Payload);
        Assert.Equal(0, child.AttemptCount);
        Assert.Equal(WebhookDeliveryStatus.Pending, child.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/webhook-deliveries/{child.Id}/redrive", null)).StatusCode);
        Factory.WithDb(db => { db.WebhookSubscriptions.Single(s => s.Id == subscription.Id).IsActive = false; db.SaveChanges(); });
        var repeated = await Client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(child.Id, (await ReadAsync<WebhookDeliveryResponse>(repeated)).Id);
        Factory.WithDb(db => { db.WebhookSubscriptions.Single(s => s.Id == subscription.Id).IsActive = true; db.SaveChanges(); });
        Guid? sentId = null;
        Factory.WebhookSender.Respond = delivery => { sentId = delivery.Id; return WebhookSendResult.Ok(200); };
        try
        {
            var attempted = await ReadAsync<List<WebhookDeliveryResponse>>(await Client.PostAsync($"/api/workspaces/{ws.Id}/webhooks/dispatch", null));
            Assert.Equal(child.Id, Assert.Single(attempted).Id);
            Assert.Equal(child.Id, sentId);
            Assert.Equal(child.Id, (await ReadAsync<WebhookDeliveryResponse>(await Client.PostAsync(route, null))).Id);
        }
        finally { Factory.WebhookSender.Reset(); }
        var original = await ReadAsync<WebhookDeliveryResponse>(await Client.GetAsync($"/api/webhook-deliveries/{id}"));
        Assert.Equal(WebhookDeliveryStatus.Failed, original.Status);
        Assert.Equal(5, original.AttemptCount);
        Assert.Equal("Exhausted", original.Error);
        Factory.WithDb(db => Assert.Single(db.WebhookDeliveries.Where(d => d.OriginalDeliveryId == id)));
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync(route, null)).StatusCode);
    }

    [Fact]
    public async Task M30_HealthPartitionsPendingAndMeasuresTerminalRows()
    {
        var ws = await CreateWorkspaceAsync();
        var active = await Subscription(ws.Id, "health");
        var paused = await Subscription(ws.Id, "paused-health");
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        Factory.FrozenTime = now;
        try
        {
            var route = $"/api/workspaces/{ws.Id}/webhooks/health";
            Assert.Null((await ReadAsync<WebhookHealthResponse>(await Client.GetAsync(route))).TerminalFailureRate);
            Factory.WithDb(db =>
            {
                db.WebhookSubscriptions.Single(s => s.Id == paused.Id).IsActive = false;
                for (var i = 0; i < 8; i++)
                {
                    var status = i < 4 ? WebhookDeliveryStatus.Pending : i < 7 ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;
                    db.WebhookDeliveries.Add(new WebhookDelivery
                    {
                        WorkspaceId = ws.Id, SubscriptionId = i == 3 ? paused.Id : active.Id,
                        EventType = WebhookEventType.MessageCreated, Payload = "Do not expose this payload", Error = "Private diagnostics",
                        Status = status, NextAttemptAt = i == 2 ? now.AddMinutes(5) : now.AddMinutes(-i),
                        DeliveredAt = status == WebhookDeliveryStatus.Succeeded ? now.AddMinutes(i - 7) : null,
                    });
                }
                db.SaveChanges();
            });
            var response = await Client.GetAsync(route);
            var health = await ReadAsync<WebhookHealthResponse>(response);
            Assert.Equal(new WebhookHealthResponse(now, 8, 4, 3, 1, 2, 1, 1, now.AddMinutes(-1), now.AddMinutes(-1), 0.25), health);
            var json = await response.Content.ReadAsStringAsync();
            foreach (var field in new[] { "\"payload\"", "\"url\"", "\"secret\"", "\"error\"" })
                Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
            Factory.WithDb(db => { db.WebhookSubscriptions.Single(s => s.Id == active.Id).IsActive = false; db.SaveChanges(); });
            var changed = await ReadAsync<WebhookHealthResponse>(await Client.GetAsync(route));
            Assert.Equal(4, changed.PausedPending);
            Assert.Equal(0, changed.ReadyPending);
            Assert.Equal(0, changed.DeferredPending);
            Assert.Null(changed.OldestReadyAt);
            Assert.Equal(8, changed.Total);
        }
        finally { Factory.FrozenTime = null; }
    }
}
