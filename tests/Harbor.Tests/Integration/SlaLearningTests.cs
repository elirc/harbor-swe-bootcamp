using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class SlaLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M17_SimulationSelectsOnePolicyAndNeverBorrowsMissingTargets()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id, slaMinutes: 60);
        var when = DateTimeOffset.Parse("2026-01-01T05:00:00-05:00");
        var route = $"/api/workspaces/{ws.Id}/sla/simulate";
        var fallback = await ReadAsync<SlaSimulationResponse>(await Client.PostAsJsonAsync(route,
            new SimulateSlaRequest(inbox.Id, ConversationPriority.Normal, when), Json));
        Assert.Equal("InboxFallback", fallback.Source);
        Assert.Equal(when.AddMinutes(60), fallback.FirstResponseDueAt);
        await CreateSlaPolicyAsync(ws.Id, "Priority", priority: ConversationPriority.High, firstResponseMinutes: 5);
        var chosen = await CreateSlaPolicyAsync(ws.Id, "Inbox resolution", inbox.Id,
            firstResponseMinutes: null, resolutionMinutes: 30);
        var policy = await ReadAsync<SlaSimulationResponse>(await Client.PostAsJsonAsync(route,
            new SimulateSlaRequest(inbox.Id, ConversationPriority.High, when), Json));
        Assert.Equal("Policy", policy.Source);
        Assert.Equal(chosen.Id, policy.PolicyId);
        Assert.Null(policy.FirstResponseDueAt);
        Assert.Equal(when.AddMinutes(30), policy.ResolutionDueAt);
        Factory.WithDb(db =>
        {
            Assert.Empty(db.Conversations.Where(c => c.WorkspaceId == ws.Id));
            Assert.Empty(db.WebhookDeliveries.Where(d => d.WorkspaceId == ws.Id));
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync(route,
            new SimulateSlaRequest(inbox.Id, ConversationPriority.Normal, DateTimeOffset.MaxValue), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync(route, new { inboxId = inbox.Id })).StatusCode);
        var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync(route,
            new SimulateSlaRequest(inbox.Id, ConversationPriority.Normal, when), Json)).StatusCode);
    }

    [Fact]
    public async Task M18_UpcomingTargetsUseOneHalfOpenWindowAndCountObligations()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var both = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var upper = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var overdue = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var resolved = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var closed = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var now = DateTimeOffset.Parse("2026-01-01T10:00:00Z");
        Factory.WithDb(db =>
        {
            var row = db.Conversations.Single(c => c.Id == both.Id);
            row.FirstResponseDueAt = now;
            row.ResolutionDueAt = now.AddMinutes(45);
            db.Conversations.Single(c => c.Id == upper.Id).FirstResponseDueAt = now.AddMinutes(60);
            db.Conversations.Single(c => c.Id == overdue.Id).FirstResponseDueAt = now.AddTicks(-1);
            row = db.Conversations.Single(c => c.Id == resolved.Id);
            row.ResolutionDueAt = now.AddMinutes(30);
            row.FirstResolvedAt = now.AddMinutes(-5);
            row = db.Conversations.Single(c => c.Id == closed.Id);
            row.State = ConversationState.Closed;
            row.FirstResponseDueAt = now.AddMinutes(10);
            db.SaveChanges();
        });
        Factory.FrozenTime = now;
        try
        {
            var route = $"/api/workspaces/{ws.Id}/sla/upcoming";
            var response = await Client.GetAsync(route + "?withinMinutes=60&pageSize=1");
            var first = Assert.Single(await ReadAsync<List<UpcomingTargetResponse>>(response));
            Assert.Equal(both.Id, first.ConversationId);
            Assert.Equal(SlaBreachKind.FirstResponse, first.Kind);
            Assert.Equal(now, first.DueAt);
            Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Total-Count")));
            Assert.Equal(now, DateTimeOffset.Parse(Assert.Single(response.Headers.GetValues("X-Evaluated-At"))));
            var second = Assert.Single(await ReadAsync<List<UpcomingTargetResponse>>(await Client.GetAsync(route + "?pageSize=1&page=2")));
            Assert.Equal(both.Id, second.ConversationId);
            Assert.Equal(SlaBreachKind.Resolution, second.Kind);
            Assert.Empty(await ReadAsync<List<UpcomingTargetResponse>>(await Client.GetAsync(route + "?state=Closed")));
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + "?withinMinutes=0")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.GetAsync(route + $"?segmentId={Guid.NewGuid()}")).StatusCode);
        }
        finally { Factory.FrozenTime = null; }
    }
}
