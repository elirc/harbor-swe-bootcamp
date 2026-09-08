using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class S08RoutingRulesTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<TeamResponse> Team(Guid ws, string name) => await ReadAsync<TeamResponse>(await Client.PostAsJsonAsync(
        $"/api/workspaces/{ws}/teams", new CreateTeamRequest(name), Json));
    private static RoutingRule Rule(Guid team, string name = "Enterprise") => new(Guid.NewGuid(), name, team,
        new SegmentRuleSet(SegmentMatch.All, [new SegmentCondition("email", SegmentOperator.EndsWith, "@enterprise.test")]));
    private Task<HttpResponseMessage> Put(Guid inbox, Guid? version, params RoutingRule[] rules) => Client.PutAsJsonAsync(
        $"/api/inboxes/{inbox}/routing-rules", new SetRoutingRulesRequest(version, rules), Json);

    [Fact]
    public async Task FirstMatchWinsForChatAndNewEmailAndPreviewHasNoEffects()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await ReadAsync<InboxResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes",
            new CreateInboxRequest("Support", 60, true, "support@example.test"), Json));
        var a = await Team(ws.Id, "Specialists A");
        var b = await Team(ws.Id, "Specialists B");
        var contact = await CreateContactAsync(ws.Id, "Ada", "ada@enterprise.test");
        var firstRule = Rule(a.Id);
        var config = await ReadAsync<RoutingRulesResponse>(await Put(inbox.Id, null, firstRule, Rule(b.Id)));
        await ReadAsync<WebhookCreatedResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/webhooks",
            new CreateWebhookRequest("https://example.test/events", [WebhookEventType.ConversationAssigned]), Json));
        var preview = await ReadAsync<RoutingDecision>(await Client.PostAsJsonAsync($"/api/inboxes/{inbox.Id}/routing-rules/preview",
            new PreviewRoutingRequest(contact.Id), Json));
        Assert.Equal(a.Id, preview.TeamId);
        Assert.Equal(firstRule.Id, preview.RuleId);
        Factory.WithDb(db =>
        {
            Assert.Empty(db.Conversations.Where(c => c.WorkspaceId == ws.Id));
            Assert.Null(db.Inboxes.Single(i => i.Id == inbox.Id).LastAssignedTeammateId);
        });
        var chat = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var mail = await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound",
            new InboundEmailRequest("new@enterprise.test", "New customer", "support@example.test", "Help", "Hello", "<routing-mail>"), Json));
        Assert.True(mail.CreatedContact);
        var emailed = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{mail.ConversationId}"));
        Assert.Equal(a.Id, chat.AssignedTeamId);
        Assert.Equal(a.Id, emailed.AssignedTeamId);
        Assert.Null(chat.AssignedTeammateId);
        Factory.WithDb(db =>
        {
            Assert.Null(db.Inboxes.Single(i => i.Id == inbox.Id).LastAssignedTeammateId);
            var events = db.AssignmentEvents.Where(e => e.ConversationId == chat.Id || e.ConversationId == emailed.Id).ToList();
            Assert.Equal(2, events.Count);
            Assert.All(events, e => { Assert.Equal(AssignmentKind.Routing, e.Kind); Assert.Equal(config.ActiveVersionId, e.RoutingVersionId); Assert.Equal(firstRule.Id, e.RoutingRuleId); });
            Assert.Equal(2, db.WebhookDeliveries.Count(d => d.WorkspaceId == ws.Id));
        });
    }

    [Fact]
    public async Task VersionsRejectStaleReplacementAndEmptyRulesRestoreFallbackWithoutRerouting()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id, autoAssign: true);
        var team = await Team(ws.Id, "Specialists");
        var contact = await CreateContactAsync(ws.Id, "Ada", "ada@enterprise.test");
        var absent = await ReadAsync<RoutingRulesResponse>(await Client.GetAsync($"/api/inboxes/{inbox.Id}/routing-rules"));
        Assert.Null(absent.Version);
        var config = await ReadAsync<RoutingRulesResponse>(await Put(inbox.Id, null, Rule(team.Id)));
        var existing = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(inbox.Id, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(inbox.Id, Guid.NewGuid())).StatusCode);
        var disabled = await ReadAsync<RoutingRulesResponse>(await Put(inbox.Id, config.Version));
        Assert.NotEqual(config.Version, disabled.Version);
        Assert.Empty(disabled.Rules);
        var next = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        Assert.NotNull(next.AssignedTeammateId);
        Assert.Null(next.AssignedTeamId);
        Assert.Equal(team.Id, (await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{existing.Id}"))).AssignedTeamId);
        Factory.WithDb(db => Assert.Equal(2, db.InboxRoutingVersions.Count(v => v.InboxId == inbox.Id)));
    }

    [Fact]
    public async Task MissingTargetsAreSkippedAndInvalidRulesNeverReplaceConfiguration()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var a = await Team(ws.Id, "A");
        var b = await Team(ws.Id, "B");
        var contact = await CreateContactAsync(ws.Id, "Ada", "ada@enterprise.test");
        var rule = Rule(a.Id);
        var config = await ReadAsync<RoutingRulesResponse>(await Put(inbox.Id, null, rule, Rule(b.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(inbox.Id, config.Version, rule, rule)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(inbox.Id, config.Version, rule with { Priority = (ConversationPriority)999 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(inbox.Id, config.Version, Rule(Guid.NewGuid()))).StatusCode);
        Factory.WithDb(db => { db.Teams.Remove(db.Teams.Single(t => t.Id == a.Id)); db.SaveChanges(); });
        var preview = await ReadAsync<RoutingDecision>(await Client.PostAsJsonAsync($"/api/inboxes/{inbox.Id}/routing-rules/preview", new PreviewRoutingRequest(contact.Id), Json));
        Assert.Equal(b.Id, preview.TeamId);
        Assert.Contains(preview.Skipped, s => s.RuleId == rule.Id && s.Reason == "UnavailableTeam");
        Assert.Equal(b.Id, (await StartConversationAsync(ws.Id, inbox.Id, contact.Id)).AssignedTeamId);
        var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"/api/inboxes/{inbox.Id}/routing-rules")).StatusCode);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/inboxes/{inbox.Id}/routing-rules")).StatusCode);
    }
}
