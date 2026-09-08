using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class SegmentLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M14_M15_PreviewAndCloneReuseDynamicMembershipWithoutChangingSource()
    {
        var ws = await CreateWorkspaceAsync();
        foreach (var name in new[] { "Ada", "Bea", "Cam" })
            await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/contacts",
                new CreateContactRequest(name, null, null, new Dictionary<string, string?>
                { ["plan"] = name == "Cam" ? "free" : "enterprise" }), Json));
        var rules = new SegmentRuleSet(SegmentMatch.All, [new SegmentCondition("attributes.plan", SegmentOperator.Equals, "enterprise")]);
        var previewResponse = await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/segments/preview", new PreviewSegmentRequest(rules, 1), Json);
        var preview = await ReadAsync<SegmentPreviewResponse>(previewResponse);
        Assert.Equal(2, preview.TotalMatches);
        Assert.Equal("Ada", Assert.Single(preview.Sample).Name);
        Assert.False(previewResponse.Headers.Contains("X-Total-Count"));
        Factory.WithDb(db => Assert.Empty(db.Segments.Where(s => s.WorkspaceId == ws.Id)));
        var source = await ReadAsync<SegmentResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/segments",
            new CreateSegmentRequest("Enterprise", rules), Json));
        var cloneResponse = await Client.PostAsJsonAsync($"/api/segments/{source.Id}/clone", new CloneSegmentRequest("  Experiment  "));
        Assert.Equal(HttpStatusCode.Created, cloneResponse.StatusCode);
        var clone = await ReadAsync<SegmentResponse>(cloneResponse);
        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("Experiment", clone.Name);
        Assert.Equal($"/api/segments/{clone.Id}", cloneResponse.Headers.Location?.AbsolutePath);
        Assert.Equal(2, (await ReadAsync<List<ContactResponse>>(await Client.GetAsync($"/api/segments/{clone.Id}/contacts"))).Count);
        var freeRules = new SegmentRuleSet(SegmentMatch.All, [new SegmentCondition("attributes.plan", SegmentOperator.Equals, "free")]);
        await ReadAsync<SegmentResponse>(await Client.PutAsJsonAsync($"/api/segments/{clone.Id}", new UpdateSegmentRequest("Experiment", freeRules), Json));
        Assert.Equal("Cam", Assert.Single(await ReadAsync<List<ContactResponse>>(await Client.GetAsync($"/api/segments/{clone.Id}/contacts"))).Name);
        Assert.Equal(2, (await ReadAsync<List<ContactResponse>>(await Client.GetAsync($"/api/segments/{source.Id}/contacts"))).Count);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync($"/api/segments/{source.Id}/clone", new CloneSegmentRequest("Experiment"))).StatusCode);
        var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync($"/api/segments/{source.Id}/clone", new CloneSegmentRequest("Agent clone"))).StatusCode);
        Assert.Equal(2, (await ReadAsync<SegmentPreviewResponse>(await Client.PostAsJsonAsync(
            $"/api/workspaces/{ws.Id}/segments/preview", new PreviewSegmentRequest(rules), Json))).TotalMatches);
    }

    [Theory]
    [InlineData("{\"rules\":null}")]
    [InlineData("{\"rules\":{\"match\":999,\"conditions\":[]}}")]
    [InlineData("{\"rules\":{\"match\":\"All\",\"conditions\":[null]}}")]
    [InlineData("{\"rules\":{\"match\":\"All\",\"conditions\":[{\"field\":\"name\",\"operator\":\"Exists\"}]},\"sampleLimit\":26}")]
    public async Task M14_MalformedPreviewIs400RatherThanAnUnhandledNull(string json)
    {
        var ws = await CreateWorkspaceAsync();
        var response = await Client.PostAsync($"/api/workspaces/{ws.Id}/segments/preview",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task M14_M16_NegativeMissingValuesAndEmptyPresenceMatchSQLite()
    {
        var ws = await CreateWorkspaceAsync();
        var contact = await CreateContactAsync(ws.Id);
        Factory.WithDb(db => { db.Contacts.Single(c => c.Id == contact.Id).Email = ""; db.SaveChanges(); });
        var rules = new SegmentRuleSet(SegmentMatch.All,
        [
            new SegmentCondition("email", SegmentOperator.Exists),
            new SegmentCondition("attributes.plan", SegmentOperator.NotEquals, "enterprise"),
            new SegmentCondition("attributes.plan", SegmentOperator.Exists),
        ]);
        var segment = await ReadAsync<SegmentResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/segments",
            new CreateSegmentRequest("Explain missing", rules), Json));
        var route = $"/api/segments/{segment.Id}/contacts/{contact.Id}/explanation";
        var explanation = await ReadAsync<SegmentExplanationResponse>(await Client.GetAsync(route));
        Assert.Equal(new[] { true, true, false }, explanation.Conditions.Select(c => c.Matches));
        Assert.Equal(new[] { 0, 1, 2 }, explanation.Conditions.Select(c => c.ConditionIndex));
        Assert.False(explanation.Matches);
        Assert.Empty(await ReadAsync<List<ContactResponse>>(await Client.GetAsync($"/api/segments/{segment.Id}/contacts")));
        await ReadAsync<SegmentResponse>(await Client.PutAsJsonAsync($"/api/segments/{segment.Id}",
            new UpdateSegmentRequest(segment.Name, rules with { Match = SegmentMatch.Any }), Json));
        Assert.True((await ReadAsync<SegmentExplanationResponse>(await Client.GetAsync(route))).Matches);
        var semanticError = new SegmentRuleSet(SegmentMatch.All, [new SegmentCondition("email", SegmentOperator.Before, "2026-01-01")]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/segments/preview",
            new PreviewSegmentRequest(semanticError), Json)).StatusCode);
        Factory.WithDb(db => { db.Segments.Single(s => s.Id == segment.Id).RulesJson = "not json"; db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/segments/{segment.Id}/clone", new CloneSegmentRequest("Bad source"))).StatusCode);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
    }
}
