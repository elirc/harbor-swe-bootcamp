using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Domain;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class S11S12SlaPlanningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<WorkingCalendarResponse> Calendar(Guid ws, params DateOnly[] closed) => await ReadAsync<WorkingCalendarResponse>(
        await Client.PostAsJsonAsync($"/api/workspaces/{ws}/working-calendars", new CreateWorkingCalendarRequest("Weekdays UTC",
            new(Enumerable.Range(1, 5).Select(d => new WorkingInterval((DayOfWeek)d, 540, 1020)).ToArray(), closed)), Json));
    private async Task<SlaRecalculationResponse> Preview(Guid ws, params Guid[] ids) => await ReadAsync<SlaRecalculationResponse>(
        await Client.PostAsJsonAsync($"/api/workspaces/{ws}/sla/recalculations/preview", new PreviewSlaRecalculationRequest(ids.ToList()), Json));
    private Task<HttpResponseMessage> Commit(Guid id) => Client.PostAsync($"/api/sla-recalculations/{id}/commit", null);

    [Fact]
    public async Task CalendarTargetsMatchSimulationAndBothIngressChannels()
    {
        var ws = await CreateWorkspaceAsync();
        var calendar = await Calendar(ws.Id, new DateOnly(2026, 9, 14));
        var inbox = await ReadAsync<InboxResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes",
            new CreateInboxRequest("Support", 60, false, "support@hours.test"), Json));
        var policy = await ReadAsync<SlaPolicyResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/sla-policies",
            new CreateSlaPolicyRequest("Working", inbox.Id, null, 60, 30, calendar.Id), Json));
        var contact = await CreateContactAsync(ws.Id, "Ada", "ada@hours.test");
        var friday = DateTimeOffset.Parse("2026-09-11T16:30:00.1234567Z");
        Factory.FrozenTime = friday;
        try
        {
            var simulation = await ReadAsync<SlaSimulationResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/sla/simulate",
                new SimulateSlaRequest(inbox.Id, ConversationPriority.Normal, friday), Json));
            var chat = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
            var mail = await ReadAsync<InboundEmailResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/email/inbound",
                new InboundEmailRequest("ada@hours.test", "Ada", "support@hours.test", "Hours", "Help", "<hours>"), Json));
            var emailed = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{mail.ConversationId}"));
            Assert.Equal(DateTimeOffset.Parse("2026-09-15T09:30:00.1234567Z"), chat.FirstResponseDueAt);
            Assert.Equal(simulation.FirstResponseDueAt, chat.FirstResponseDueAt);
            Assert.Equal(chat.FirstResponseDueAt, emailed.FirstResponseDueAt);
            Assert.Equal(calendar.Id, chat.AppliedWorkingCalendarId);
            Assert.Equal(calendar.Id, emailed.AppliedWorkingCalendarId);
            (await Client.PutAsJsonAsync($"/api/sla-policies/{policy.Id}", new UpdateSlaPolicyRequest("Elapsed", inbox.Id, null, 60, 30), Json)).EnsureSuccessStatusCode();
            Assert.Equal(chat.FirstResponseDueAt, (await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{chat.Id}"))).FirstResponseDueAt);
        }
        finally { Factory.FrozenTime = null; }
    }

    [Fact]
    public async Task ReapplyUsesCreationTimeKeepsHistoryAndReplaysItsExactResult()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id, slaMinutes: 120);
        var contact = await CreateContactAsync(ws.Id); var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        BackdateConversation(conversation.Id, TimeSpan.FromHours(4));
        var policy = await CreateSlaPolicyAsync(ws.Id, firstResponseMinutes: 30);
        var plan = await Preview(ws.Id, conversation.Id);
        Assert.Equal(plan.Items[0].OldFirstResponseDueAt!.Value.AddMinutes(-90), plan.Items[0].Proposed.FirstResponseDueAt);
        Factory.WithDb(db => Assert.Equal(plan.Items[0].OldFirstResponseDueAt, db.Conversations.Single(c => c.Id == conversation.Id).FirstResponseDueAt));
        var response = await Commit(plan.Id); var body = await response.Content.ReadAsStringAsync(); response.EnsureSuccessStatusCode();
        Assert.Equal(body, await (await Commit(plan.Id)).Content.ReadAsStringAsync());
        Factory.WithDb(db =>
        {
            var current = db.Conversations.Single(c => c.Id == conversation.Id);
            Assert.Equal(ConversationPriority.Normal, current.Priority);
            Assert.Equal(plan.Items[0].Proposed.FirstResponseDueAt, current.FirstResponseDueAt);
            Assert.Equal(policy.Id, current.SlaPolicyId);
            Assert.Single(db.SlaBreachEvents.Where(e => e.ConversationId == conversation.Id));
        });
    }

    [Fact]
    public async Task ChangedConfigurationExpiryAndFailedCommitLeaveTargetsUntouched()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id, slaMinutes: 120);
        var contact = await CreateContactAsync(ws.Id); var a = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var b = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var stale = await Preview(ws.Id, a.Id);
        await CreateSlaPolicyAsync(ws.Id, firstResponseMinutes: 30);
        Assert.Equal(HttpStatusCode.Conflict, (await Commit(stale.Id)).StatusCode);
        var fresh = await Preview(ws.Id, a.Id, b.Id);
        Factory.FailCommand = command => command.CommandText.Contains("UPDATE \"SlaRecalculationPlans\"");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Commit(fresh.Id)).StatusCode); }
        finally { Factory.FailCommand = null; }
        Factory.WithDb(db =>
        {
            Assert.Equal(a.FirstResponseDueAt, db.Conversations.Single(c => c.Id == a.Id).FirstResponseDueAt);
            Assert.Equal(b.FirstResponseDueAt, db.Conversations.Single(c => c.Id == b.Id).FirstResponseDueAt);
            Assert.Null(db.SlaRecalculationPlans.Single(p => p.Id == fresh.Id).CommittedAt);
        });
        Factory.FrozenTime = fresh.ExpiresAt;
        try { Assert.Equal(HttpStatusCode.Gone, (await Commit(fresh.Id)).StatusCode); }
        finally { Factory.FrozenTime = null; }
    }
}
