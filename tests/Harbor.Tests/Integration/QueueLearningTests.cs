using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class QueueLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task AssertQueue(Guid workspaceId, string query, params Guid[] expected)
    {
        var route = $"/api/workspaces/{workspaceId}/conversations";
        var response = await Client.GetAsync(route + "?" + query);
        var actual = await ReadAsync<List<ConversationSummaryResponse>>(response);
        Assert.Equal(expected.Order(), actual.Select(c => c.Id).Order());
        Assert.Equal(expected.Length.ToString(), Assert.Single(response.Headers.GetValues("X-Total-Count")));
        var overview = await ReadAsync<ConversationOverviewResponse>(await Client.GetAsync(route + "/overview?" + query));
        Assert.Equal(expected.Length, overview.Total);
        Assert.Equal(overview.Total, overview.Open + overview.Snoozed + overview.Closed);
    }

    [Fact]
    public async Task US16_SubjectPresenceIncludesWhitespaceAndComposesWithReplySearch()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var named = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id, "Invoice");
        var missing = new List<Guid>();
        foreach (var subject in new string?[] { null, "", " \t\u00a0\u2003" })
            missing.Add((await StartConversationAsync(workspace.Id, inbox.Id, contact.Id, subject, "Invoice question")).Id);
        await AssertQueue(workspace.Id, "hasSubject=true", named.Id);
        await AssertQueue(workspace.Id, "hasSubject=false&q=invoice", missing.ToArray());
        await AssertQueue(workspace.Id, "", [named.Id, .. missing]);
        var page = await Client.GetAsync($"/api/workspaces/{workspace.Id}/conversations?hasSubject=false&pageSize=1");
        Assert.Single(await ReadAsync<List<ConversationSummaryResponse>>(page));
        Assert.Equal("3", Assert.Single(page.Headers.GetValues("X-Total-Count")));
    }

    [Fact]
    public async Task US17_OnlyTheFirstTeammateReplyEndsTheWait()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var agent = await CreateTeammateAsync(workspace.Id);
        var noted = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var replied = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{noted.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Note, "Investigating"), Json));
        await ReadAsync<ConversationSummaryResponse>(await Client.PostAsJsonAsync($"/api/conversations/{noted.Id}/state",
            new ChangeStateRequest(ConversationState.Closed, null), Json));
        await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{replied.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Reply, "Here is the answer"), Json));
        await AssertQueue(workspace.Id, "awaitingFirstResponse=true", noted.Id);
        await AssertQueue(workspace.Id, "awaitingFirstResponse=false", replied.Id);
        await AssertQueue(workspace.Id, "awaitingFirstResponse=true&state=Closed", noted.Id);
    }

    [Fact]
    public async Task US18_US19_TagExistenceAndEitherSlaTargetAreIndependent()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var tagged = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var resolutionOnly = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var plain = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        await TagConversationAsync(tagged.Id, (await CreateTagAsync(workspace.Id, "Billing")).Id);
        await TagConversationAsync(tagged.Id, (await CreateTagAsync(workspace.Id, "Urgent")).Id);
        Factory.WithDb(db =>
        {
            db.Conversations.Single(c => c.Id == tagged.Id).FirstResponseDueAt = DateTimeOffset.UtcNow.AddMinutes(30);
            db.Conversations.Single(c => c.Id == resolutionOnly.Id).ResolutionDueAt = DateTimeOffset.UtcNow.AddHours(2);
            db.SaveChanges();
        });
        await AssertQueue(workspace.Id, "hasTags=true", tagged.Id);
        await AssertQueue(workspace.Id, "hasTags=false", resolutionOnly.Id, plain.Id);
        await AssertQueue(workspace.Id, "hasSlaTarget=true", tagged.Id, resolutionOnly.Id);
        await AssertQueue(workspace.Id, "hasSlaTarget=false", plain.Id);
        await AssertQueue(workspace.Id, "hasTags=false&hasSlaTarget=true", resolutionOnly.Id);
    }

    [Fact]
    public async Task US20_UpdatedWindowIsHalfOpenAndUsesInstants()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var start = DateTimeOffset.Parse("2026-01-01T10:00:00Z");
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add((await StartConversationAsync(workspace.Id, inbox.Id, contact.Id)).Id);
        Factory.WithDb(db =>
        {
            for (var i = 0; i < ids.Count; i++) db.Conversations.Single(c => c.Id == ids[i]).UpdatedAt = start.AddHours(i);
            db.SaveChanges();
        });
        await AssertQueue(workspace.Id, "updatedFrom=2026-01-01T05:00:00-05:00&updatedBefore=2026-01-01T12:00:00Z", ids[0], ids[1]);
        foreach (var query in new[] {
            "updatedFrom=2026-01-01&updatedBefore=2026-01-01",
            "updatedFrom=2026-01-02&updatedBefore=2026-01-01", "updatedFrom=banana" })
            foreach (var suffix in new[] { "", "/overview" })
                Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(
                    $"/api/workspaces/{workspace.Id}/conversations{suffix}?{query}")).StatusCode);
    }

    [Fact]
    public async Task US21_PriorityOrdersBeforeActivityAndUsesAnIdTieBreaker()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var normal = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var older = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var newer = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        await SetPriorityAsync(older.Id, ConversationPriority.Urgent);
        await SetPriorityAsync(newer.Id, ConversationPriority.Urgent);
        var now = DateTimeOffset.UtcNow;
        Factory.WithDb(db =>
        {
            db.Conversations.Single(c => c.Id == normal.Id).LastMessageAt = now;
            db.Conversations.Single(c => c.Id == older.Id).LastMessageAt = now.AddDays(-2);
            db.Conversations.Single(c => c.Id == newer.Id).LastMessageAt = now.AddDays(-1);
            db.SaveChanges();
        });
        var route = $"/api/workspaces/{workspace.Id}/conversations?sort=PriorityFirst";
        var rows = await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(route));
        Assert.Equal(new[] { newer.Id, older.Id, normal.Id }, rows.Select(c => c.Id));
        Assert.Equal(older.Id, Assert.Single(await ReadAsync<List<ConversationSummaryResponse>>(
            await Client.GetAsync(route + "&pageSize=1&page=2"))).Id);
        Factory.WithDb(db =>
        {
            db.Conversations.Single(c => c.Id == older.Id).LastMessageAt = now.AddDays(-1);
            db.SaveChanges();
        });
        rows = await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(route));
        // GUID ordering in SQLite follows its canonical text representation.
        Assert.Equal(new[] { newer.Id, older.Id }.OrderBy(id => id.ToString(), StringComparer.Ordinal), rows.Take(2).Select(c => c.Id));
    }

    [Theory]
    [InlineData("hasSubject")]
    [InlineData("awaitingFirstResponse")]
    [InlineData("hasTags")]
    [InlineData("hasSlaTarget")]
    public async Task US16_US17_US18_US19_BadBooleanIsRejectedByBothReaders(string field)
    {
        var workspace = await CreateWorkspaceAsync();
        foreach (var suffix in new[] { "", "/overview" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(
                $"/api/workspaces/{workspace.Id}/conversations{suffix}?{field}=banana")).StatusCode);
    }

    [Fact]
    public async Task US22_US23_US25_MessageReadsPageFilterCountAndCheckTheParent()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var agent = await CreateTeammateAsync(workspace.Id);
        var conversation = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var note = await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Note, "Private investigation"), Json));
        var reply = await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Reply, "Answer"), Json));
        var route = $"/api/conversations/{conversation.Id}";
        var page = await Client.GetAsync(route + "/messages?pageSize=1&page=2&kind=Reply");
        Assert.Equal(reply.Id, Assert.Single(await ReadAsync<List<MessageResponse>>(page)).Id);
        Assert.Equal("2", Assert.Single(page.Headers.GetValues("X-Total-Count")));
        Assert.Equal(note.Id, Assert.Single(await ReadAsync<List<MessageResponse>>(
            await Client.GetAsync(route + "/messages?kind=Note"))).Id);
        Assert.Equal(new MessageCountsResponse(3, 2, 1), await ReadAsync<MessageCountsResponse>(
            await Client.GetAsync(route + "/message-counts?kind=Note&pageSize=1")));
        Assert.Equal(3, (await ReadAsync<ConversationDetailResponse>(await Client.GetAsync(route))).Messages.Count);
        Assert.Empty(await ReadAsync<List<MessageResponse>>(await Client.GetAsync(route + "/messages?page=100&pageSize=1")));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + "/messages?kind=999")).StatusCode);
        await CreateWorkspaceAsync();
        foreach (var id in new[] { conversation.Id, Guid.NewGuid() })
            foreach (var suffix in new[] { "/messages", "/message-counts" })
                Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/conversations/{id}{suffix}")).StatusCode);
    }

    [Fact]
    public async Task US24_ContactOverviewIsAnUnfilteredWorkspaceAggregate()
    {
        var workspace = await CreateWorkspaceAsync();
        var route = $"/api/workspaces/{workspace.Id}/contacts/overview";
        Assert.Equal(new ContactOverviewResponse(0, 0, 0), await ReadAsync<ContactOverviewResponse>(await Client.GetAsync(route)));
        await CreateContactAsync(workspace.Id, "Ada", "ada@example.test");
        var blank = await CreateContactAsync(workspace.Id, "Bea");
        await CreateContactAsync(workspace.Id, "Cam");
        Factory.WithDb(db =>
        {
            db.Contacts.Single(c => c.Id == blank.Id).Email = "\t\u00a0";
            db.SaveChanges();
        });
        var foreign = await CreateWorkspaceAsync();
        await CreateContactAsync(foreign.Id, "Foreign", "foreign@example.test");
        ActAsAdminOf(workspace.Id);
        Assert.Equal(new ContactOverviewResponse(3, 1, 2), await ReadAsync<ContactOverviewResponse>(
            await Client.GetAsync(route + "?q=absent&hasEmail=true&page=100&pageSize=1")));
    }
}
