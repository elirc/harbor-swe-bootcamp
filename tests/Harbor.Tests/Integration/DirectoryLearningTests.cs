using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class DirectoryLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task US06_RenameChangesOnlyTheNameAndEnforcesOwnership()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id, "Original", 30, true);
        var renamed = await ReadAsync<InboxResponse>(await Client.PutAsJsonAsync(
            $"/api/workspaces/{workspace.Id}/inboxes/{inbox.Id}/name", new { name = "  New  name  " }));
        Assert.Equal("New  name", renamed.Name);
        Assert.Equal(inbox.AutoAssign, renamed.AutoAssign);
        Assert.Equal(inbox.FirstResponseSlaMinutes, renamed.FirstResponseSlaMinutes);
        foreach (var name in new[] { "", "   ", new string('x', 201) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(
                $"/api/workspaces/{workspace.Id}/inboxes/{inbox.Id}/name", new { name })).StatusCode);

        var foreign = await CreateWorkspaceAsync();
        var foreignInbox = await CreateInboxAsync(foreign.Id);
        ActAsAdminOf(workspace.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync(
            $"/api/workspaces/{workspace.Id}/inboxes/{foreignInbox.Id}/name", new { name = "Forbidden" })).StatusCode);
        var agent = await CreateTeammateAsync(workspace.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PutAsJsonAsync(
            $"/api/workspaces/{workspace.Id}/inboxes/{inbox.Id}/name", new { name = "Forbidden" })).StatusCode);
    }

    [Fact]
    public async Task US07_US08_US09_TeammateFiltersComposeAndNeverExposeKeys()
    {
        var workspace = await CreateWorkspaceAsync();
        var ada = await CreateTeammateAsync(workspace.Id, "Ada", "ada@billing.example");
        await CreateTeammateAsync(workspace.Id, "Bea", "bea@billing.example");
        await SetAvailabilityAsync(ada.Id, TeammateAvailability.Away);
        var response = await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/teammates?q=%20BILLING%20&availability=Away&role=Agent&pageSize=1");
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("apiKey", raw, StringComparison.OrdinalIgnoreCase);
        var people = await ReadAsync<List<TeammateResponse>>(response);
        Assert.Equal(ada.Id, Assert.Single(people).Id);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        var admins = await ReadAsync<List<TeammateResponse>>(await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/teammates?role=Admin"));
        Assert.Equal("Boot Admin", Assert.Single(admins).Name);
    }

    [Theory]
    [InlineData("availability=999")]
    [InlineData("availability=unavailable")]
    [InlineData("role=999")]
    [InlineData("role=owner")]
    public async Task US08_US09_InvalidEnumsAreRejected(string query)
    {
        var workspace = await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/teammates?{query}")).StatusCode);
    }

    [Fact]
    public async Task US10_TeamSearchPreservesTheRoster()
    {
        var workspace = await CreateWorkspaceAsync();
        var person = await CreateTeammateAsync(workspace.Id);
        var team = await CreateTeamAsync(workspace.Id, "Billing North");
        await CreateTeamAsync(workspace.Id, "Product");
        (await Client.PostAsJsonAsync($"/api/teams/{team.Id}/members", new { teammateId = person.Id }))
            .EnsureSuccessStatusCode();
        var response = await Client.GetAsync($"/api/workspaces/{workspace.Id}/teams?q=%20BILLING%20");
        var found = Assert.Single(await ReadAsync<List<TeamResponse>>(response));
        Assert.Equal(team.Id, found.Id);
        Assert.Contains(person.Id, found.MemberIds);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
    }

    [Fact]
    public async Task US11_US12_InboxSearchAndFlagsComposeBeforePaging()
    {
        var workspace = await CreateWorkspaceAsync();
        await CreateInboxAsync(workspace.Id, "Billing Manual", autoAssign: false);
        var wanted = await CreateInboxAsync(workspace.Id, "Billing Automatic", autoAssign: true);
        await CreateInboxAsync(workspace.Id, "Product", autoAssign: true);
        var response = await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/inboxes?q=%20BILLING%20&autoAssign=true&pageSize=1");
        Assert.Equal(wanted.Id, Assert.Single(await ReadAsync<List<InboxResponse>>(response)).Id);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        Assert.Single(await ReadAsync<List<InboxResponse>>(await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/inboxes?autoAssign=false")));
        Assert.Equal(3, (await ReadAsync<List<InboxResponse>>(await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/inboxes"))).Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/inboxes?autoAssign=banana")).StatusCode);
    }

    [Fact]
    public async Task US13_AutoAssignmentToggleAffectsFutureStartsOnly()
    {
        var workspace = await CreateWorkspaceAsync();
        var bootstrap = Assert.Single(await ReadAsync<List<TeammateResponse>>(await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/teammates")));
        await SetAvailabilityAsync(bootstrap.Id, TeammateAvailability.Away);
        var agent = await CreateTeammateAsync(workspace.Id);
        var inbox = await CreateInboxAsync(workspace.Id);
        var contact = await CreateContactAsync(workspace.Id);
        var original = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var route = $"/api/workspaces/{workspace.Id}/inboxes/{inbox.Id}/auto-assign";
        foreach (var body in new object[] { new { }, new { autoAssign = (bool?)null } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(route, body)).StatusCode);
        for (var i = 0; i < 2; i++)
            Assert.True((await ReadAsync<InboxResponse>(await Client.PutAsJsonAsync(route, new { autoAssign = true }))).AutoAssign);
        var assigned = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        await ReadAsync<InboxResponse>(await Client.PutAsJsonAsync(route, new { autoAssign = false }));
        var later = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        Factory.WithDb(db =>
        {
            Assert.Null(db.Conversations.Single(c => c.Id == original.Id).AssignedTeammateId);
            Assert.Equal(agent.Id, db.Conversations.Single(c => c.Id == assigned.Id).AssignedTeammateId);
            Assert.Null(db.Conversations.Single(c => c.Id == later.Id).AssignedTeammateId);
        });
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PutAsJsonAsync(route, new { autoAssign = true })).StatusCode);
    }

    [Fact]
    public async Task US14_TagSearchLeavesStoredNamesAlone()
    {
        var workspace = await CreateWorkspaceAsync();
        var tag = await CreateTagAsync(workspace.Id, "Billing Priority");
        await CreateTagAsync(workspace.Id, "Product");
        var response = await Client.GetAsync($"/api/workspaces/{workspace.Id}/tags?q=%20BILLING%20");
        var actual = Assert.Single(await ReadAsync<List<TagResponse>>(response));
        Assert.Equal(tag.Id, actual.Id);
        Assert.Equal(tag.Name, actual.Name);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
    }

    [Fact]
    public async Task US15_ShortcutIsExactAndCombinesWithTextSearch()
    {
        var workspace = await CreateWorkspaceAsync();
        var route = $"/api/workspaces/{workspace.Id}/canned-replies";
        var refund = await ReadAsync<CannedReplyResponse>(await Client.PostAsJsonAsync(route,
            new CreateCannedReplyRequest("refund", "Refund help", "Please contact billing."), Json));
        await ReadAsync<CannedReplyResponse>(await Client.PostAsJsonAsync(route,
            new CreateCannedReplyRequest("refund-late", "Late refund", "Please wait."), Json));
        var response = await Client.GetAsync(route + "?shortcut=%20REFUND%20&q=billing&pageSize=1");
        Assert.Equal(refund.Id, Assert.Single(await ReadAsync<List<CannedReplyResponse>>(response)).Id);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        Assert.Empty(await ReadAsync<List<CannedReplyResponse>>(await Client.GetAsync(route + "?shortcut=refund&q=wait")));
        Assert.Empty(await ReadAsync<List<CannedReplyResponse>>(await Client.GetAsync(route + "?shortcut=unknown")));
        Assert.Equal(2, (await ReadAsync<List<CannedReplyResponse>>(await Client.GetAsync(route + "?shortcut=%20%20"))).Count);
    }
}
