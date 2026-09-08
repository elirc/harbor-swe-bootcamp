using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class TriageLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M05_BulkPriorityValidatesEveryIdBeforeChangingAnyAndUsesEachInbox()
    {
        var ws = await CreateWorkspaceAsync();
        var inboxA = await CreateInboxAsync(ws.Id, "A", 120);
        var inboxB = await CreateInboxAsync(ws.Id, "B", 120);
        var contact = await CreateContactAsync(ws.Id);
        var a = await StartConversationAsync(ws.Id, inboxA.Id, contact.Id);
        var b = await StartConversationAsync(ws.Id, inboxB.Id, contact.Id);
        await CreateSlaPolicyAsync(ws.Id, "A urgent", inboxA.Id, ConversationPriority.Urgent, 15);
        await CreateSlaPolicyAsync(ws.Id, "B urgent", inboxB.Id, ConversationPriority.Urgent, 30);
        var route = $"/api/workspaces/{ws.Id}/conversations/bulk-priority";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync(route,
            new BulkPriorityRequest([a.Id, Guid.NewGuid()], ConversationPriority.Urgent), Json)).StatusCode);
        Factory.WithDb(db =>
        {
            var unchanged = db.Conversations.Single(c => c.Id == a.Id);
            Assert.Equal(ConversationPriority.Normal, unchanged.Priority);
            Assert.Equal(a.FirstResponseDueAt, unchanged.FirstResponseDueAt);
            Assert.Empty(db.SlaBreachEvents.Where(e => e.ConversationId == a.Id));
        });
        var response = await ReadAsync<List<ConversationSummaryResponse>>(await Client.PostAsJsonAsync(route,
            new BulkPriorityRequest([a.Id, b.Id], ConversationPriority.Urgent), Json));
        Assert.Equal(2, response.Count);
        Assert.All(response, row => Assert.Equal(ConversationPriority.Urgent, row.Priority));
        Assert.Equal(a.CreatedAt.AddMinutes(15), response.Single(c => c.Id == a.Id).FirstResponseDueAt);
        Assert.Equal(b.CreatedAt.AddMinutes(30), response.Single(c => c.Id == b.Id).FirstResponseDueAt);
        foreach (var request in new[] {
            new BulkPriorityRequest([], ConversationPriority.High), new BulkPriorityRequest([a.Id, a.Id], ConversationPriority.High),
            new BulkPriorityRequest([a.Id], null), new BulkPriorityRequest([a.Id], (ConversationPriority)999),
            new BulkPriorityRequest(Enumerable.Range(0, 26).Select(_ => Guid.NewGuid()).ToArray(), ConversationPriority.High) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync(route, request, Json)).StatusCode);
    }

    [Fact]
    public async Task M06_ReplacementChangesLinksAtomicallyAndPreservesTagRows()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var a = await CreateTagAsync(ws.Id, "A");
        var b = await CreateTagAsync(ws.Id, "B");
        var c = await CreateTagAsync(ws.Id, "C");
        await TagConversationAsync(conversation.Id, a.Id);
        await TagConversationAsync(conversation.Id, b.Id);
        var route = $"/api/conversations/{conversation.Id}/tags";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PutAsJsonAsync(route,
            new ReplaceTagsRequest([c.Id, Guid.NewGuid()]))).StatusCode);
        Factory.WithDb(db => Assert.Equal(new[] { a.Id, b.Id }.Order(),
            db.ConversationTags.Where(t => t.ConversationId == conversation.Id).Select(t => t.TagId).ToArray().Order()));
        for (var i = 0; i < 2; i++)
        {
            var result = await ReadAsync<ConversationSummaryResponse>(await Client.PutAsJsonAsync(route, new ReplaceTagsRequest([b.Id, c.Id])));
            Assert.Equal(new[] { "b", "c" }, result.Tags);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(route, new ReplaceTagsRequest([a.Id, a.Id]))).StatusCode);
        Assert.Empty((await ReadAsync<ConversationSummaryResponse>(await Client.PutAsJsonAsync(route, new ReplaceTagsRequest([])))).Tags);
        Factory.WithDb(db =>
        {
            Assert.Empty(db.ConversationTags.Where(t => t.ConversationId == conversation.Id));
            Assert.Equal(3, db.Tags.Count(t => t.WorkspaceId == ws.Id));
        });
    }

    [Fact]
    public async Task M08_WakeUsesAnInclusiveBoundaryAndBoundedRepeatableBatches()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var conversations = new List<ConversationDetailResponse>();
        for (var i = 0; i < 4; i++) conversations.Add(await StartConversationAsync(ws.Id, inbox.Id, contact.Id));
        var now = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        Factory.WithDb(db =>
        {
            for (var i = 0; i < 4; i++)
            {
                var row = db.Conversations.Single(c => c.Id == conversations[i].Id);
                row.State = ConversationState.Snoozed;
                row.SnoozedUntil = now.AddTicks(i - 2);
            }
            db.SaveChanges();
        });
        IReadOnlyList<Guid> first = [], second = [], third = [];
        Factory.WithDb(db => first = new SnoozeWake(db).WakeAsync(ws.Id, 2, now).GetAwaiter().GetResult());
        Factory.WithDb(db => second = new SnoozeWake(db).WakeAsync(ws.Id, 2, now).GetAwaiter().GetResult());
        Factory.WithDb(db => third = new SnoozeWake(db).WakeAsync(ws.Id, 2, now).GetAwaiter().GetResult());
        Assert.Equal(conversations.Take(2).Select(c => c.Id), first);
        Assert.Equal(conversations[2].Id, Assert.Single(second));
        Assert.Empty(third);
        Factory.WithDb(db =>
        {
            Assert.Equal(ConversationState.Snoozed, db.Conversations.Single(c => c.Id == conversations[3].Id).State);
            foreach (var original in conversations)
                Assert.Equal(original.LastMessageAt, db.Conversations.Single(c => c.Id == original.Id).LastMessageAt);
        });
        var route = $"/api/workspaces/{ws.Id}/conversations/wake-due";
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync(route + "?limit=201", null)).StatusCode);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync(route, null)).StatusCode);
    }

    [Fact]
    public async Task M09_TimelinePagesAcrossSourcesAndKeepsIdenticalIdsDistinct()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var opening = Assert.Single(conversation.Messages);
        var note = await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Note, "Investigation"), Json));
        var start = DateTimeOffset.Parse("2026-01-01T10:00:00Z");
        var breachId = Guid.NewGuid();
        Factory.WithDb(db =>
        {
            db.Messages.Single(m => m.Id == opening.Id).CreatedAt = start;
            db.Messages.Single(m => m.Id == note.Id).CreatedAt = start.AddMinutes(30);
            db.AssignmentEvents.Add(new AssignmentEvent { Id = opening.Id, ConversationId = conversation.Id,
                ActorTeammateId = agent.Id, Kind = AssignmentKind.Manual, CreatedAt = start });
            db.SlaBreachEvents.Add(new SlaBreachEvent { Id = breachId, ConversationId = conversation.Id,
                Kind = SlaBreachKind.FirstResponse, DueAt = start.AddMinutes(15), BreachedAt = start.AddMinutes(20), CreatedAt = start.AddHours(2) });
            db.SaveChanges();
        });
        var route = $"/api/conversations/{conversation.Id}/timeline";
        var firstResponse = await Client.GetAsync(route + "?pageSize=2");
        var first = await ReadAsync<List<TimelineResponse>>(firstResponse);
        Assert.Equal("4", Assert.Single(firstResponse.Headers.GetValues("X-Total-Count")));
        Assert.Equal(new[] { note.Id, breachId }, first.Select(e => e.Id));
        Assert.Equal(start.AddMinutes(20), first[1].OccurredAt);
        var second = await ReadAsync<List<TimelineResponse>>(await Client.GetAsync(route + "?pageSize=2&page=2"));
        Assert.Equal(new[] { TimelineSource.Message, TimelineSource.Assignment }, second.Select(e => e.Kind));
        Assert.All(second, e => Assert.Equal(opening.Id, e.Id));
        Assert.NotNull(second[0].Message);
        Assert.Null(second[0].Assignment);
        Assert.NotNull(second[1].Assignment);
        Assert.Empty(await ReadAsync<List<TimelineResponse>>(await Client.GetAsync(route + "?page=100")));
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task M10_EligibilityCountsOpenAndSnoozedAndNeverAdvancesRotation()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var agent = await CreateTeammateAsync(ws.Id, "Capacity agent");
        await SetAvailabilityAsync(agent.Id, TeammateAvailability.Away, 2);
        var a = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var b = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var closed = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        foreach (var row in new[] { a, b, closed }) await AssignAsync(row.Id, agent.Id);
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{b.Id}/state",
            new ChangeStateRequest(ConversationState.Snoozed, DateTimeOffset.UtcNow.AddDays(1)), Json));
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{closed.Id}/state",
            new ChangeStateRequest(ConversationState.Closed, null), Json));
        Factory.WithDb(db => { db.Inboxes.Single(i => i.Id == inbox.Id).LastAssignedTeammateId = agent.Id; db.SaveChanges(); });
        var route = $"/api/workspaces/{ws.Id}/assignment-eligibility";
        for (var i = 0; i < 2; i++)
        {
            var response = await Client.GetAsync(route);
            Assert.DoesNotContain("apiKey", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            var rows = await ReadAsync<List<EligibilityResponse>>(response);
            Assert.Contains(rows, t => t.Name == "Boot Admin");
            var found = rows.Single(t => t.Id == agent.Id);
            Assert.Equal(2, found.AssignedCount);
            Assert.False(found.Eligible);
            Assert.Equal(new[] { "Away", "AtCapacity" }, found.Reasons);
        }
        Factory.WithDb(db => Assert.Equal(agent.Id, db.Inboxes.Single(i => i.Id == inbox.Id).LastAssignedTeammateId));
        await SetAvailabilityAsync(agent.Id, TeammateAvailability.Available, null);
        var unlimited = (await ReadAsync<List<EligibilityResponse>>(await Client.GetAsync(route))).Single(t => t.Id == agent.Id);
        Assert.True(unlimited.Eligible);
        Assert.Empty(unlimited.Reasons);
    }
}
