using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain.Entities;

namespace Harbor.Tests.Integration;

public class S07ContactImportTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private Task<HttpResponseMessage> Stage(Guid ws, params object?[] rows) =>
        Client.PostAsJsonAsync($"/api/workspaces/{ws}/contact-imports", new { rows }, Json);

    [Fact]
    public async Task StagingReportsEveryBadRowAndCannotPartiallyCommit()
    {
        var ws = await CreateWorkspaceAsync();
        var response = await Stage(ws.Id, new { name = "Ada", email = "ada@example.test" },
            new { name = " ", email = "not-email" }, null, new { name = "Wrong shape", email = 42 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await ReadAsync<ContactImportResponse>(response);
        Assert.Equal(ContactImportStatus.Invalid, batch.Status);
        Assert.Equal(new[] { 1, 2, 3, 4 }, batch.Rows.Select(r => r.RowNumber));
        Assert.Empty(batch.Rows[0].Errors);
        Assert.Contains(batch.Rows[1].Errors, e => e.Field == "Name");
        Assert.Contains(batch.Rows[1].Errors, e => e.Field == "Email");
        Assert.NotEmpty(batch.Rows[2].Errors);
        Assert.NotEmpty(batch.Rows[3].Errors);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null)).StatusCode);
        Factory.WithDb(db => Assert.Empty(db.Contacts.Where(c => c.WorkspaceId == ws.Id)));
    }

    [Fact]
    public async Task ReadyBatchCommitsOnceWithStableRowMappingAndAllowsDuplicateEmails()
    {
        var ws = await CreateWorkspaceAsync();
        var batch = await ReadAsync<ContactImportResponse>(await Stage(ws.Id,
            new { name = " Ada ", email = "same@example.test", externalId = "E-1", attributes = new { plan = "pro" } },
            new { name = "Sam", email = "same@example.test", externalId = "E-1" }));
        Assert.Equal(ContactImportStatus.Ready, batch.Status);
        Factory.WithDb(db => Assert.Empty(db.Contacts.Where(c => c.WorkspaceId == ws.Id)));
        var committed = await ReadAsync<ContactImportResponse>(await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null));
        var repeated = await ReadAsync<ContactImportResponse>(await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null));
        Assert.Equal(ContactImportStatus.Committed, committed.Status);
        Assert.Equal(committed.CommittedAt, repeated.CommittedAt);
        Assert.Equal(committed.Rows.Select(r => r.ContactId), repeated.Rows.Select(r => r.ContactId));
        Assert.Equal(2, committed.Rows.Select(r => r.ContactId).Distinct().Count());
        Assert.All(committed.Rows, r => Assert.NotNull(r.ContactId));
        var contact = await ReadAsync<ContactResponse>(await Client.GetAsync($"/api/contacts/{committed.Rows[0].ContactId}"));
        Assert.Equal("Ada", contact.Name);
        Assert.Equal("pro", contact.Attributes["plan"]);
        Factory.WithDb(db => Assert.Equal(2, db.Contacts.Count(c => c.WorkspaceId == ws.Id)));
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/contact-imports/{batch.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null)).StatusCode);
    }

    [Fact]
    public async Task BoundsExpiryAndRoleAreEnforced()
    {
        var ws = await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Stage(ws.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Stage(ws.Id, Enumerable.Range(0, 201).Select(i => (object?)new { name = "Row " + i }).ToArray())).StatusCode);
        var batch = await ReadAsync<ContactImportResponse>(await Stage(ws.Id, new { name = "Ada" }));
        Factory.FrozenTime = batch.ExpiresAt;
        try { Assert.Equal(HttpStatusCode.Gone, (await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null)).StatusCode); }
        finally { Factory.FrozenTime = null; }
        var agent = await CreateTeammateAsync(ws.Id);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await Stage(ws.Id, new { name = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"/api/contact-imports/{batch.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsync($"/api/contact-imports/{batch.Id}/commit", null)).StatusCode);
    }
}
