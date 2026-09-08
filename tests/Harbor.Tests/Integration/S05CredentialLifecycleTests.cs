using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Infrastructure;

namespace Harbor.Tests.Integration;

public class S05CredentialLifecycleTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private Task<HttpResponseMessage> Create(Guid teammate, string name = "Laptop", int lifetime = 15) =>
        Client.PostAsJsonAsync($"/api/teammates/{teammate}/api-keys", new CreateApiKeyRequest(name, lifetime), Json);

    [Fact]
    public async Task RotationOverlapsThenRevokesOnlyOneKeyAndNeverReadsBackSecrets()
    {
        var ws = await CreateWorkspaceAsync();
        var teammate = await CreateTeammateAsync(ws.Id);
        ActAs(teammate.ApiKey);
        var created = await ReadAsync<CreatedApiKeyResponse>(await Create(teammate.Id, "  Laptop  "));
        Assert.Equal("Laptop", created.Credential.Name);
        Assert.NotEqual(teammate.ApiKey, created.ApiKey);
        var listResponse = await Client.GetAsync($"/api/teammates/{teammate.Id}/api-keys");
        var metadata = await ReadAsync<List<ApiKeyMetadata>>(listResponse);
        Assert.Equal(2, metadata.Count);
        var initial = metadata.Single(k => k.ExpiresAt is null);
        var text = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.ApiKey, text);
        Assert.DoesNotContain(ApiKeys.Hash(created.ApiKey), text);
        ActAs(created.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/teammates/{teammate.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/api-keys/{initial.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/api-keys/{initial.Id}")).StatusCode);
        ActAs(teammate.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync($"/api/teammates/{teammate.Id}")).StatusCode);
        ActAs(created.ApiKey);
        // Revoking the calling credential is allowed; only the next request is unauthenticated.
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/api-keys/{created.Credential.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync($"/api/teammates/{teammate.Id}")).StatusCode);
        ActAsAdminOf(ws.Id);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/teammates/{teammate.Id}/api-keys")).StatusCode);
    }

    [Fact]
    public async Task ExpiryUsesExactBoundaryAndCurrentRoleAppliesToEveryKey()
    {
        var ws = await CreateWorkspaceAsync();
        var teammate = await CreateTeammateAsync(ws.Id);
        var now = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        Factory.FrozenTime = now;
        try
        {
            var key = await ReadAsync<CreatedApiKeyResponse>(await Create(teammate.Id));
            ActAs(key.ApiKey);
            Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes", new CreateInboxRequest("Forbidden", null), Json)).StatusCode);
            Factory.WithDb(db => { db.Teammates.Single(t => t.Id == teammate.Id).Role = TeammateRole.Admin; db.SaveChanges(); });
            Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/inboxes", new CreateInboxRequest("Allowed", null), Json)).StatusCode);
            Factory.FrozenTime = now.AddMinutes(15).AddTicks(-1);
            Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/teammates/{teammate.Id}")).StatusCode);
            Factory.FrozenTime = now.AddMinutes(15);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync($"/api/teammates/{teammate.Id}")).StatusCode);
        }
        finally { Factory.FrozenTime = null; }
    }

    [Fact]
    public async Task LimitsAndOwnershipAreEnforcedAndRevocationFreesASlot()
    {
        var ws = await CreateWorkspaceAsync();
        var owner = await CreateTeammateAsync(ws.Id);
        var other = await CreateTeammateAsync(ws.Id);
        var created = new List<CreatedApiKeyResponse>();
        ActAs(owner.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(other.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"/api/teammates/{other.Id}/api-keys")).StatusCode);
        for (var i = 0; i < 4; i++) created.Add(await ReadAsync<CreatedApiKeyResponse>(await Create(owner.Id, "Client " + i)));
        Assert.Equal(HttpStatusCode.Conflict, (await Create(owner.Id)).StatusCode);
        ActAs(other.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.DeleteAsync($"/api/api-keys/{created[0].Credential.Id}")).StatusCode);
        ActAs(owner.ApiKey);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/api-keys/{created[0].Credential.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(owner.Id)).StatusCode);
        foreach (var lifetime in new[] { 14, 43_201 }) Assert.Equal(HttpStatusCode.BadRequest, (await Create(owner.Id, lifetime: lifetime)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(owner.Id, "   ")).StatusCode);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Create(owner.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/api-keys/{created[0].Credential.Id}")).StatusCode);
    }
}
