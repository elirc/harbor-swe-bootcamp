using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Domain;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class S16ConfigurationAuditTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task AuditCapturesActualActorsAndChangesWithoutCannedContent()
    {
        var ws = await CreateWorkspaceAsync(); var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        await SetAvailabilityAsync(agent.Id, TeammateAvailability.Away, 4);
        await SetAvailabilityAsync(agent.Id, TeammateAvailability.Away, 4);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"/api/workspaces/{ws.Id}/configuration-audit")).StatusCode);
        ActAsAdminOf(ws.Id);
        var team = await ReadAsync<TeamResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/teams", new CreateTeamRequest("Support"), Json));
        (await Client.PostAsJsonAsync($"/api/teams/{team.Id}/members", new AddTeamMemberRequest(agent.Id), Json)).EnsureSuccessStatusCode();
        (await Client.PostAsJsonAsync($"/api/teams/{team.Id}/members", new AddTeamMemberRequest(agent.Id), Json)).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/teams/{team.Id}/members/{agent.Id}")).EnsureSuccessStatusCode();
        var canned = await ReadAsync<CannedReplyResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/canned-replies", new CreateCannedReplyRequest("private-shortcut", "Private title", "Secret body A"), Json));
        (await Client.PutAsJsonAsync($"/api/canned-replies/{canned.Id}", new UpdateCannedReplyRequest(canned.Shortcut, canned.Title, "Secret body B"), Json)).EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/canned-replies/{canned.Id}", new UpdateCannedReplyRequest(canned.Shortcut, canned.Title, "Secret body B"), Json)).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/canned-replies/{canned.Id}")).EnsureSuccessStatusCode();
        var auditResponse = await Client.GetAsync($"/api/workspaces/{ws.Id}/configuration-audit");
        var text = await auditResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret body", text); Assert.DoesNotContain("Private title", text); Assert.DoesNotContain("private-shortcut", text);
        var rows = await ReadAsync<List<ConfigurationAuditResponse>>(auditResponse);
        Assert.Equal(6, rows.Count);
        var availability = Assert.Single(rows, e => e.EntityKind == ConfigurationEntityKind.TeammateAvailability);
        Assert.Equal(agent.Id, availability.ActorTeammateId); Assert.Equal(2, availability.ChangedFields.Count);
        Assert.Equal(3, rows.Count(e => e.EntityId == canned.Id));
        Factory.WithDb(db => Assert.All(db.ConfigurationAuditEvents.Where(e => e.WorkspaceId == ws.Id), e => Assert.DoesNotContain("Secret body", e.AllowedChangesJson ?? "")));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync($"/api/workspaces/{ws.Id}/configuration-audit?action=99")).StatusCode);
    }

    [Fact]
    public async Task FailedConfigurationSaveLeavesNoAuditAndFiltersUseHalfOpenTime()
    {
        var ws = await CreateWorkspaceAsync(); var agent = await CreateTeammateAsync(ws.Id);
        var now = DateTimeOffset.UtcNow;
        Factory.FrozenTime = now;
        try
        {
            Factory.FailCommand = command => command.CommandText.Contains("UPDATE \"Teammates\"");
            try { Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PutAsJsonAsync($"/api/teammates/{agent.Id}/availability", new UpdateAvailabilityRequest(TeammateAvailability.Away, 2), Json)).StatusCode); }
            finally { Factory.FailCommand = null; }
            Factory.WithDb(db => { Assert.Empty(db.ConfigurationAuditEvents.Where(e => e.WorkspaceId == ws.Id)); Assert.Equal(TeammateAvailability.Available, db.Teammates.Single(t => t.Id == agent.Id).Availability); });
            await SetAvailabilityAsync(agent.Id, TeammateAvailability.Away, 2);
            Assert.Single(await ReadAsync<List<ConfigurationAuditResponse>>(await Client.GetAsync($"/api/workspaces/{ws.Id}/configuration-audit?from={Uri.EscapeDataString(now.ToString("O"))}")));
            Assert.Empty(await ReadAsync<List<ConfigurationAuditResponse>>(await Client.GetAsync($"/api/workspaces/{ws.Id}/configuration-audit?before={Uri.EscapeDataString(now.ToString("O"))}")));
        }
        finally { Factory.FrozenTime = null; }
    }
}
