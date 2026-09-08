using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class ConversationQueueTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record QueueFixture(
        Guid WorkspaceId, Guid InboxId, Guid ContactId, Guid TeammateId, Guid TeamId,
        Guid SegmentId, Guid[] Ids);

    private async Task<QueueFixture> SeedQueueAsync()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var otherInbox = await CreateInboxAsync(workspace.Id, "Other");
        var contact = await CreateContactAsync(workspace.Id, "Alpha");
        var otherContact = await CreateContactAsync(workspace.Id, "Beta");
        var teammate = await CreateTeammateAsync(workspace.Id);
        var team = await CreateTeamAsync(workspace.Id, "Escalations");
        var tag = await CreateTagAsync(workspace.Id, "billing");
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var conversation = await StartConversationAsync(
                workspace.Id, i < 2 ? inbox.Id : otherInbox.Id,
                i < 2 ? contact.Id : otherContact.Id,
                i == 0 ? "alpha subject" : "beta subject", i == 1 ? "alpha reply" : "beta reply");
            ids.Add(conversation.Id);
        }

        await TagConversationAsync(ids[0], tag.Id);
        await TagConversationAsync(ids[2], tag.Id);
        var segmentId = Guid.NewGuid();
        Factory.WithDb(db =>
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var c = db.Conversations.Single(c => c.Id == ids[i]);
                c.CreatedAt = Start.AddDays(i);
                c.LastMessageAt = Start.AddDays(10); // Deliberately equal for ordering tests.
                if (i == 0) c.FirstResponseDueAt = Start.AddHours(1);
                if (i == 1)
                {
                    c.Snooze(Start.AddYears(10), Start);
                    c.AssignToTeammate(teammate.Id, Start);
                    c.Priority = ConversationPriority.High;
                    c.Channel = MessageChannel.Email;
                }
                if (i == 2)
                {
                    c.Close(Start.AddDays(3));
                    c.AssignToTeam(team.Id, Start);
                    c.Priority = ConversationPriority.Urgent;
                    c.ResolutionDueAt = Start.AddDays(2).AddHours(1);
                }
            }

            db.Messages.Add(new Message
            {
                ConversationId = ids[2], Body = "alpha internal secret",
                Kind = MessageKind.Note, AuthorType = AuthorType.Teammate,
                AuthorTeammateId = teammate.Id,
            });
            db.Segments.Add(new Segment
            {
                Id = segmentId, WorkspaceId = workspace.Id, Name = "Alpha contacts",
                RulesJson = """{"match":"All","conditions":[{"field":"name","operator":"Equals","value":"Alpha"}]}""",
            });
            db.SaveChanges();
        });

        // A matching conversation in another tenant must never influence counts.
        var foreign = await CreateWorkspaceAsync("Foreign");
        var foreignInbox = await CreateInboxAsync(foreign.Id);
        var foreignContact = await CreateContactAsync(foreign.Id);
        await StartConversationAsync(foreign.Id, foreignInbox.Id, foreignContact.Id, "alpha subject");
        ActAsAdminOf(workspace.Id);
        return new(workspace.Id, inbox.Id, contact.Id, teammate.Id, team.Id, segmentId, ids.ToArray());
    }

    public static TheoryData<string, int[]> Filters => new()
    {
        { "", [0, 1, 2, 3] },
        { "state=Open", [0, 3] },
        { "state=Snoozed", [1] },
        { "state=Closed", [2] },
        { "unassigned=true", [0, 3] },
        { "unassigned=false", [0, 1, 2, 3] },
        { "q=ALPHA", [0, 1] },
        { "tag=BILLING", [0, 2] },
        { "priority=High", [1] },
        { "channel=Email", [1] },
        { "slaBreached=true", [0, 2] },
        { "slaBreached=false", [0, 1, 2, 3] },
        { "inboxId={inbox}", [0, 1] },
        { "contactId={contact}", [0, 1] },
        { "assignedTeammateId={teammate}", [1] },
        { "assignedTeamId={team}", [2] },
        { "segmentId={segment}", [0, 1] },
        { "createdFrom=2026-01-02T00:00:00Z&createdBefore=2026-01-04T00:00:00Z", [1, 2] },
        { "createdFrom=2026-01-01T19:00:00-05:00&createdBefore=2026-01-03T19:00:00-05:00", [1, 2] },
        { "createdBefore=2026-01-02T00:00:00Z", [0] },
        { "createdFrom=2026-01-04T00:00:00Z", [3] },
        { "q=alpha&tag=billing&state=Open", [0] },
        { "q=no-match", [] },
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task ListAndOverview_ShareFilters_AndCountTheWholeSet(string filter, int[] expected)
    {
        var queue = await SeedQueueAsync();
        filter = filter.Replace("{inbox}", queue.InboxId.ToString())
            .Replace("{contact}", queue.ContactId.ToString())
            .Replace("{teammate}", queue.TeammateId.ToString())
            .Replace("{team}", queue.TeamId.ToString())
            .Replace("{segment}", queue.SegmentId.ToString());
        var route = $"/api/workspaces/{queue.WorkspaceId}/conversations";
        var listResponse = await Client.GetAsync($"{route}?{filter}&sort=OldestCreated");
        var list = await ReadAsync<List<ConversationSummaryResponse>>(listResponse);
        var overview = await ReadAsync<ConversationOverviewResponse>(
            await Client.GetAsync($"{route}/overview?{filter}&page=2147483647&pageSize=1"));

        Assert.Equal(expected.Select(i => queue.Ids[i]), list.Select(c => c.Id));
        Assert.Equal(expected.Length.ToString(), listResponse.Headers.GetValues(Paging.TotalCountHeader).Single());
        Assert.Equal(new ConversationOverviewResponse(
            expected.Length, expected.Count(i => i is 0 or 3), expected.Count(i => i == 1),
            expected.Count(i => i == 2), expected.Count(i => i is 0 or 3)), overview);
    }

    [Theory]
    [InlineData("RecentActivity")]
    [InlineData("OldestCreated")]
    [InlineData("NewestCreated")]
    public async Task EqualTimestamps_HaveStablePageBoundaries(string sort)
    {
        var queue = await SeedQueueAsync();
        Factory.WithDb(db =>
        {
            foreach (var c in db.Conversations.Where(c => c.WorkspaceId == queue.WorkspaceId))
                c.CreatedAt = Start;
            db.SaveChanges();
        });
        var actual = new List<Guid>();
        for (var page = 1; page <= 4; page++)
        {
            var rows = await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(
                $"/api/workspaces/{queue.WorkspaceId}/conversations?sort={sort}&page={page}&pageSize=1"));
            actual.Add(Assert.Single(rows).Id);
        }
        Assert.Equal(queue.Ids.OrderBy(id => id.ToString(), StringComparer.Ordinal), actual);
    }

    [Fact]
    public async Task NewestCreated_OrdersByCreation_NotLastActivity()
    {
        var queue = await SeedQueueAsync();
        var rows = await ReadAsync<List<ConversationSummaryResponse>>(await Client.GetAsync(
            $"/api/workspaces/{queue.WorkspaceId}/conversations?sort=NewestCreated"));
        Assert.Equal(queue.Ids.Reverse(), rows.Select(c => c.Id));
    }

    [Theory]
    [InlineData(2147483647, 200)]
    [InlineData(21474838, 200)]
    public async Task HugePage_ReturnsEmptyArray_NotWrappedOffset(int page, int size)
    {
        var queue = await SeedQueueAsync();
        var response = await Client.GetAsync(
            $"/api/workspaces/{queue.WorkspaceId}/conversations?page={page}&pageSize={size}");
        Assert.Empty(await ReadAsync<List<ConversationSummaryResponse>>(response));
        Assert.Equal("4", response.Headers.GetValues(Paging.TotalCountHeader).Single());
        Assert.Equal(page.ToString(), response.Headers.GetValues(Paging.PageHeader).Single());
    }

    [Theory]
    [InlineData("createdFrom=2026-02-01&createdBefore=2026-01-01")]
    [InlineData("createdFrom=2026-01-01&createdBefore=2026-01-01")]
    [InlineData("createdFrom=not-a-date")]
    [InlineData("sort=Surprise")]
    [InlineData("sort=999")]
    public async Task InvalidQuery_ReturnsValidationProblem_OnBothReads(string filter)
    {
        var workspace = await CreateWorkspaceAsync();
        foreach (var suffix in new[] { "", "/overview" })
        {
            var response = await Client.GetAsync($"/api/workspaces/{workspace.Id}/conversations{suffix}?{filter}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Overview_RequiresAuthentication_AllowsAgents_AndScopesWorkspace()
    {
        var workspace = await CreateWorkspaceAsync();
        var agent = await CreateTeammateAsync(workspace.Id);
        var foreign = await CreateWorkspaceAsync("Other");
        ActAs(agent.ApiKey);
        Assert.Equal(new ConversationOverviewResponse(0, 0, 0, 0, 0),
            await ReadAsync<ConversationOverviewResponse>(await Client.GetAsync(
                $"/api/workspaces/{workspace.Id}/conversations/overview")));
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(
            $"/api/workspaces/{foreign.Id}/conversations/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(
            $"/api/workspaces/{Guid.NewGuid()}/conversations/overview")).StatusCode);
        Client.DefaultRequestHeaders.Remove("X-Api-Key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/conversations/overview")).StatusCode);
    }

    [Fact]
    public async Task ForeignAndMissingSegments_ReturnSameProblem_OnBothReads()
    {
        var queue = await SeedQueueAsync();
        var other = await CreateWorkspaceAsync();
        foreach (var suffix in new[] { "", "/overview" })
        {
            foreach (var segment in new[] { queue.SegmentId, Guid.NewGuid() })
            {
                var response = await Client.GetAsync(
                    $"/api/workspaces/{other.Id}/conversations{suffix}?segmentId={segment}");
                Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
                var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
                Assert.Equal("Unknown segment", problem!.Title);
            }
        }
    }
}
