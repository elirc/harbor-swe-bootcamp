using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class ReportLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M19_BacklogBucketsAreExhaustiveAndIncludeExactBoundaries()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var ages = new[] { 23.99, 24, 71.99, 72, -1, 100 };
        var ids = new List<Guid>();
        foreach (var age in ages)
        {
            var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
            ids.Add(conversation.Id);
            SetConversationTimings(conversation.Id, now.AddHours(-age));
        }
        Factory.WithDb(db =>
        {
            db.Conversations.Single(c => c.Id == ids[1]).State = ConversationState.Snoozed;
            db.Conversations.Single(c => c.Id == ids[5]).State = ConversationState.Closed;
            db.SaveChanges();
        });
        Factory.FrozenTime = now;
        try
        {
            var route = $"/api/workspaces/{ws.Id}/reports/backlog-age";
            var report = await ReadAsync<BacklogAgeResponse>(await Client.GetAsync(route));
            Assert.Equal(new BacklogAgeResponse(now, 5, 4, 1, 2, 2, 1), report);
            var empty = await ReadAsync<BacklogAgeResponse>(await Client.GetAsync(route + $"?inboxId={Guid.NewGuid()}"));
            Assert.Equal(0, empty.Total);
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + "?from=2026-02-01&to=2026-01-01")).StatusCode);
        }
        finally { Factory.FrozenTime = null; }
    }

    [Fact]
    public async Task M20_TeamWorkloadDoesNotAttributeAnIndividualsWorkToEveryTeam()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var a = await CreateTeamAsync(ws.Id, "A team");
        var b = await CreateTeamAsync(ws.Id, "B team");
        foreach (var team in new[] { a, b })
            (await Client.PostAsJsonAsync($"/api/teams/{team.Id}/members", new AddTeamMemberRequest(agent.Id), Json)).EnsureSuccessStatusCode();
        var direct = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var personal = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        await AssignAsync(direct.Id, teamId: a.Id);
        await AssignAsync(personal.Id, teammateId: agent.Id);
        var route = $"/api/workspaces/{ws.Id}/reports/team-workload";
        var rows = await ReadAsync<List<TeamWorkloadResponse>>(await Client.GetAsync(route));
        Assert.Equal(new TeamWorkloadResponse(a.Id, a.Name, 1, 1, 1, 1, 0, 0), rows[0]);
        Assert.Equal(new TeamWorkloadResponse(b.Id, b.Name, 1, 1, 0, 0, 0, 0), rows[1]);
        var page = await Client.GetAsync(route + "?pageSize=1&page=2");
        Assert.Equal(b.Id, Assert.Single(await ReadAsync<List<TeamWorkloadResponse>>(page)).Id);
        Assert.Equal("2", Assert.Single(page.Headers.GetValues("X-Total-Count")));
        var onlyPersonal = await ReadAsync<List<TeamWorkloadResponse>>(await Client.GetAsync(route + $"?assignedTeammateId={agent.Id}"));
        Assert.All(onlyPersonal, row => Assert.Equal(0, row.Conversations));
    }

    [Fact]
    public async Task M21_CsvMatchesJsonIncludingGapsAndLateResolutionEvents()
    {
        var ws = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id);
        var a = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var b = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var start = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        SetConversationTimings(a.Id, start, firstResolvedAt: start.AddDays(4));
        SetConversationTimings(b.Id, start.AddDays(2));
        var route = $"/api/workspaces/{ws.Id}/reports/volume";
        var query = "?from=2026-01-01&to=2026-01-04&interval=Day";
        var json = await ReadAsync<VolumeReportResponse>(await Client.GetAsync(route + query));
        var previousCulture = CultureInfo.CurrentCulture;
        HttpResponseMessage response;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            response = await Client.GetAsync(route + ".csv" + query);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("conversation-volume.csv", response.Content.Headers.ContentDisposition?.FileNameStar);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("bucket_start_utc,bucket_end_utc,started,closed", lines[0]);
        Assert.Equal(json.Points.Count + 1, lines.Length);
        for (var i = 0; i < json.Points.Count; i++)
        {
            var fields = lines[i + 1].Split(',');
            Assert.Equal(json.Points[i].BucketStart, DateTimeOffset.Parse(fields[0], CultureInfo.InvariantCulture));
            Assert.Equal(json.Points[i].BucketEnd, DateTimeOffset.Parse(fields[1], CultureInfo.InvariantCulture));
            Assert.Equal(json.Points[i].Started, int.Parse(fields[2], CultureInfo.InvariantCulture));
            Assert.Equal(json.Points[i].Closed, int.Parse(fields[3], CultureInfo.InvariantCulture));
        }
        Assert.Equal(5, json.Points.Count);
        Assert.Equal(0, json.Points[1].Started);
        Assert.Equal(1, json.Points[4].Closed);
        var empty = await Client.GetStringAsync(route + $".csv?inboxId={Guid.NewGuid()}");
        Assert.Equal(lines[0] + "\r\n", empty);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + ".csv?interval=999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(route + ".csv?from=2026-01-01&to=2026-01-01")).StatusCode);
    }
}
