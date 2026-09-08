using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class ContactInsightsLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M11_PatchRetainsOmittedKeysAndDetectsStaleOrInvalidChanges()
    {
        var ws = await CreateWorkspaceAsync();
        var contact = await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/contacts",
            new CreateContactRequest("Ada", "ada@example.test", "crm-1", new Dictionary<string, string?>
            { ["plan"] = "pro", ["region"] = "eu", ["language"] = "en" }), Json));
        var route = $"/api/contacts/{contact.Id}/attributes";
        var v1 = await ReadAsync<ContactAttributesResponse>(await Client.GetAsync(route));
        var changes = new Dictionary<string, string?> { [" plan "] = "enterprise", ["region"] = null, ["Tier"] = " gold " };
        var v2 = await ReadAsync<ContactAttributesResponse>(await Client.PatchAsJsonAsync(route, new PatchAttributesRequest(changes, v1.Version)));
        Assert.Equal("enterprise", v2.Attributes["plan"]);
        Assert.Equal("en", v2.Attributes["language"]);
        Assert.Equal(" gold ", v2.Attributes["Tier"]);
        Assert.False(v2.Attributes.ContainsKey("region"));
        Assert.NotEqual(v1.Version, v2.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PatchAsJsonAsync(route, new PatchAttributesRequest(changes, v1.Version))).StatusCode);
        var collisions = new Dictionary<string, string?> { ["plan"] = "free", [" plan "] = "other" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PatchAsJsonAsync(route, new PatchAttributesRequest(collisions, v2.Version))).StatusCode);
        var overflow = Enumerable.Range(0, 6).ToDictionary(i => "key" + i, _ => (string?)new string('x', 2_000));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PatchAsJsonAsync(route, new PatchAttributesRequest(overflow, v2.Version))).StatusCode);
        var unchanged = await ReadAsync<ContactAttributesResponse>(await Client.PatchAsJsonAsync(route,
            new PatchAttributesRequest(new Dictionary<string, string?>(), v2.Version)));
        Assert.Equal(v2.Version, unchanged.Version);
        var detail = await ReadAsync<ContactResponse>(await Client.GetAsync($"/api/contacts/{contact.Id}"));
        Assert.Equal(contact.Name, detail.Name);
        Assert.Equal(contact.Email, detail.Email);
        Assert.Equal(contact.ExternalId, detail.ExternalId);
        Assert.Equal(contact.LastSeenAt, detail.LastSeenAt);
        var segment = await ReadAsync<SegmentResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/segments",
            new CreateSegmentRequest("Enterprise", new SegmentRuleSet(SegmentMatch.All,
                [new SegmentCondition("attributes.plan", SegmentOperator.Equals, "enterprise")])), Json));
        Assert.Equal(contact.Id, Assert.Single(await ReadAsync<List<ContactResponse>>(
            await Client.GetAsync($"/api/segments/{segment.Id}/contacts"))).Id);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task M12_DuplicateGroupsRankAndMembersDisappearWhenOnlyOneRemains()
    {
        var ws = await CreateWorkspaceAsync();
        var ids = new List<Guid>();
        foreach (var name in new[] { "Ada", "Bea", "Cam", "Dev", "Eve", "Unique", "Blank" })
            ids.Add((await CreateContactAsync(ws.Id, name)).Id);
        Factory.WithDb(db =>
        {
            string?[] emails = [" A@example.test ", "a@example.test", "B@example.test", " b@example.test ", "b@example.test", "unique@example.test", "\t\u00a0"];
            for (var i = 0; i < ids.Count; i++) db.Contacts.Single(c => c.Id == ids[i]).Email = emails[i];
            db.SaveChanges();
        });
        var foreign = await CreateWorkspaceAsync();
        await CreateContactAsync(foreign.Id, "Foreign", "b@example.test");
        ActAsAdminOf(ws.Id);
        var route = $"/api/workspaces/{ws.Id}/contacts/duplicate-emails";
        var response = await Client.GetAsync(route + "?pageSize=1");
        Assert.Equal(new DuplicateEmailResponse("b@example.test", 3), Assert.Single(await ReadAsync<List<DuplicateEmailResponse>>(response)));
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        Assert.Equal("a@example.test", Assert.Single(await ReadAsync<List<DuplicateEmailResponse>>(
            await Client.GetAsync(route + "?pageSize=1&page=2"))).NormalizedEmail);
        var members = await Client.GetAsync(route + "/members?email=%20B%40example.test%20&pageSize=1&page=2");
        Assert.Equal(ids[3], Assert.Single(await ReadAsync<List<ContactResponse>>(members)).Id);
        Assert.Equal("3", Assert.Single(members.Headers.GetValues("X-Total-Count")));
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/contacts/{ids[0]}" )).StatusCode);
        var noLongerDuplicate = await Client.GetAsync(route + "/members?email=a%40example.test");
        Assert.Empty(await ReadAsync<List<ContactResponse>>(noLongerDuplicate));
        Assert.Equal("0", Assert.Single(noLongerDuplicate.Headers.GetValues("X-Total-Count")));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + "/members?email=%20%20")).StatusCode);
    }

    [Fact]
    public async Task M13_SupportSummaryCountsParentsAndMessagesSeparately()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var route = $"/api/contacts/{contact.Id}/support-summary";
        Assert.Equal(new ContactSupportResponse(0, 0, 0, 0, 0, 0, null, null, null),
            await ReadAsync<ContactSupportResponse>(await Client.GetAsync(route)));
        var a = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var b = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var c = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        foreach (var kind in new[] { MessageKind.Reply, MessageKind.Reply, MessageKind.Note })
            await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{a.Id}/messages",
                new AddMessageRequest(AuthorType.Teammate, agent.Id, kind, "More context"), Json));
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{b.Id}/state",
            new ChangeStateRequest(ConversationState.Snoozed, DateTimeOffset.UtcNow.AddDays(1)), Json));
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{c.Id}/state",
            new ChangeStateRequest(ConversationState.Closed, null), Json));
        var summary = await ReadAsync<ContactSupportResponse>(await Client.GetAsync(route));
        Assert.Equal(3, summary.TotalConversations);
        Assert.Equal(1, summary.Open);
        Assert.Equal(1, summary.Snoozed);
        Assert.Equal(1, summary.Closed);
        Assert.Equal(5, summary.Replies);
        Assert.Equal(1, summary.Notes);
        Assert.Equal(a.Id, summary.LatestConversationId);
        Assert.Equal(a.CreatedAt, summary.FirstConversationAt);
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{c.Id}/state",
            new ChangeStateRequest(ConversationState.Open, null), Json));
        var reopened = await ReadAsync<ContactSupportResponse>(await Client.GetAsync(route));
        Assert.Equal(2, reopened.Open);
        Assert.Equal(0, reopened.Closed);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task M07_TransferPreservesIdentityAndMeasuresNewSlaFromOriginalCreation()
    {
        var ws = await CreateWorkspaceAsync();
        var source = await CreateInboxAsync(ws.Id, "Source", 120);
        var destination = await CreateInboxAsync(ws.Id, "Destination", 30);
        var contact = await CreateContactAsync(ws.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, source.Id, contact.Id);
        await AssignAsync(conversation.Id, agent.Id);
        BackdateConversation(conversation.Id, TimeSpan.FromHours(1));
        var before = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        var route = $"/api/conversations/{conversation.Id}/transfer";
        var moved = await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync(route, new TransferConversationRequest(destination.Id)));
        Assert.Equal(destination.Id, moved.InboxId);
        Assert.Equal(before.CreatedAt.AddMinutes(30), moved.FirstResponseDueAt);
        Assert.Equal(before.LastMessageAt, moved.LastMessageAt);
        Assert.Equal(agent.Id, moved.AssignedTeammateId);
        Assert.Equal(before.ContactId, moved.ContactId);
        Assert.Equal(before.State, moved.State);
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync(route, new TransferConversationRequest(destination.Id)));
        var history = await ReadAsync<List<TransferEventResponse>>(await Client.GetAsync($"/api/conversations/{conversation.Id}/transfer-events"));
        Assert.Equal(source.Id, Assert.Single(history).FromInboxId);
        Assert.Equal(destination.Id, history[0].ToInboxId);
        Factory.WithDb(db =>
        {
            Assert.Single(db.Messages.Where(m => m.ConversationId == conversation.Id));
            Assert.Single(db.SlaBreachEvents.Where(e => e.ConversationId == conversation.Id));
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync(route, new TransferConversationRequest(Guid.NewGuid()))).StatusCode);
        Factory.WithDb(db => { db.Conversations.Single(c => c.Id == conversation.Id).Channel = MessageChannel.Email; db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync(route, new TransferConversationRequest(source.Id))).StatusCode);
    }
}
