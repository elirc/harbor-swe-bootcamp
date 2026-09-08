using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

/// <summary>
/// US01-US05: examples shared with the contact-query bootcamp lesson.
/// Every assertion uses a fresh workspace so shared test-class storage is harmless.
/// </summary>
public class ContactLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData("crm-42")]
    [InlineData(" ADA ")]
    [InlineData("billing.example")]
    public async Task US01_SearchAlternativesStayInsideTheWorkspace(string q)
    {
        var own = await CreateWorkspaceAsync();
        var ada = await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync(
            $"/api/workspaces/{own.Id}/contacts",
            new CreateContactRequest("Ada", "ada@billing.example", "CRM-420"), Json));
        await CreateContactAsync(own.Id, "Bea");
        var foreign = await CreateWorkspaceAsync();
        await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync(
            $"/api/workspaces/{foreign.Id}/contacts",
            new CreateContactRequest("Ada", "ada@billing.example", "CRM-420"), Json));
        ActAsAdminOf(own.Id);

        var response = await Client.GetAsync(
            $"/api/workspaces/{own.Id}/contacts?q={Uri.EscapeDataString(q)}");
        var contacts = await ReadAsync<List<ContactResponse>>(response);

        Assert.Equal(ada.Id, Assert.Single(contacts).Id);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
    }

    [Theory]
    [InlineData("hasEmail", "true", 1)]
    [InlineData("hasEmail", "false", 3)]
    [InlineData("hasEmail", null, 4)]
    [InlineData("hasExternalId", "true", 1)]
    [InlineData("hasExternalId", "false", 3)]
    [InlineData("hasExternalId", null, 4)]
    public async Task US02_US03_PresenceHasTrueFalseAndOmittedMeanings(
        string field, string? value, int expectedCount)
    {
        var own = await CreateWorkspaceAsync();
        var contacts = new List<ContactResponse>();
        foreach (var name in new[] { "Ada", "Bea", "Cam", "Dev" })
            contacts.Add(await CreateContactAsync(own.Id, name));

        Factory.WithDb(db =>
        {
            string?[] values = ["ada@example.test", null, "", "   "];
            for (var i = 0; i < contacts.Count; i++)
            {
                var row = db.Contacts.Single(c => c.Id == contacts[i].Id);
                row.Email = values[i];
                row.ExternalId = values[i];
            }
            db.SaveChanges();
        });
        var foreign = await CreateWorkspaceAsync();
        await CreateContactAsync(foreign.Id, "Foreign");
        ActAsAdminOf(own.Id);

        var query = value is null ? "" : $"?{field}={value}";
        var response = await Client.GetAsync($"/api/workspaces/{own.Id}/contacts{query}");
        var actual = await ReadAsync<List<ContactResponse>>(response);
        var expected = value switch
        {
            "true" => contacts.Take(1),
            "false" => contacts.Skip(1),
            _ => contacts,
        };

        Assert.Equal(expected.Select(c => c.Id), actual.Select(c => c.Id));
        Assert.Equal(expectedCount.ToString(), Assert.Single(response.Headers.GetValues("X-Total-Count")));
    }

    [Theory]
    [InlineData("hasEmail")]
    [InlineData("hasExternalId")]
    [InlineData("hasConversations")]
    public async Task US02_US03_US04_InvalidBooleanIsRejected(string field)
    {
        var workspace = await CreateWorkspaceAsync();
        var response = await Client.GetAsync($"/api/workspaces/{workspace.Id}/contacts?{field}=banana");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("\t\r\n", false)]
    [InlineData("\u00a0\u2003", false)]
    [InlineData(" \tx\u2003", true)]
    public async Task US02_US03_WhitespaceMeansMoreThanAnAsciiSpace(string text, bool present)
    {
        var workspace = await CreateWorkspaceAsync();
        var contact = await CreateContactAsync(workspace.Id);
        Factory.WithDb(db =>
        {
            var row = db.Contacts.Single(c => c.Id == contact.Id);
            row.Email = text;
            row.ExternalId = text;
            db.SaveChanges();
        });
        foreach (var field in new[] { "hasEmail", "hasExternalId" })
        {
            var result = await ReadAsync<List<ContactResponse>>(await Client.GetAsync(
                $"/api/workspaces/{workspace.Id}/contacts?{field}={present.ToString().ToLowerInvariant()}"));
            Assert.Equal(contact.Id, Assert.Single(result).Id);
            var opposite = await ReadAsync<List<ContactResponse>>(await Client.GetAsync(
                $"/api/workspaces/{workspace.Id}/contacts?{field}={(!present).ToString().ToLowerInvariant()}"));
            Assert.Empty(opposite);
        }
    }

    [Fact]
    public async Task US01_US02_US03_FiltersComposeBeforeCountingAndPaging()
    {
        var workspace = await CreateWorkspaceAsync();
        var ids = new List<Guid>();
        foreach (var name in new[] { "Ada", "Bea", "Cam" })
        {
            var contact = await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync(
                $"/api/workspaces/{workspace.Id}/contacts",
                new CreateContactRequest(name, name == "Ada" ? "ada@example.test" : null, "CRM-42"), Json));
            ids.Add(contact.Id);
        }
        await CreateContactAsync(workspace.Id, "No match");
        var response = await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/contacts?q=crm&hasEmail=false&hasExternalId=true&page=2&pageSize=1");
        var actual = await ReadAsync<List<ContactResponse>>(response);

        Assert.Equal(ids[2], Assert.Single(actual).Id);
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("X-Page")));
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Page-Size")));
        var unchanged = await ReadAsync<ContactResponse>(await Client.GetAsync($"/api/contacts/{ids[2]}"));
        Assert.Equal("CRM-42", unchanged.ExternalId);
    }

    [Fact]
    public async Task US04_ConversationExistenceIncludesClosedWithoutDuplicatingContacts()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id);
        var ada = await CreateContactAsync(workspace.Id, "Ada");
        var bea = await CreateContactAsync(workspace.Id, "Bea");
        var cam = await CreateContactAsync(workspace.Id, "Cam");
        await StartConversationAsync(workspace.Id, inbox.Id, ada.Id);
        await StartConversationAsync(workspace.Id, inbox.Id, ada.Id);
        var closed = await StartConversationAsync(workspace.Id, inbox.Id, bea.Id);
        (await Client.PostAsJsonAsync($"/api/conversations/{closed.Id}/state",
            new ChangeStateRequest(ConversationState.Closed, null), Json)).EnsureSuccessStatusCode();

        var withResponse = await Client.GetAsync($"/api/workspaces/{workspace.Id}/contacts?hasConversations=true");
        var withHistory = await ReadAsync<List<ContactResponse>>(withResponse);
        var without = await ReadAsync<List<ContactResponse>>(await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/contacts?hasConversations=false"));

        Assert.Equal(new[] { ada.Id, bea.Id }, withHistory.Select(c => c.Id));
        Assert.Equal("2", Assert.Single(withResponse.Headers.GetValues("X-Total-Count")));
        Assert.Equal(cam.Id, Assert.Single(without).Id);
    }

    [Fact]
    public async Task US05_CreateAndUpdateTrimOnlyOutsideTheName()
    {
        var workspace = await CreateWorkspaceAsync();
        var created = await ReadAsync<ContactResponse>(await Client.PostAsJsonAsync(
            $"/api/workspaces/{workspace.Id}/contacts",
            new CreateContactRequest("  Ada  Lovelace  ", "ada@example.test", "Ref-1"), Json));
        Assert.Equal("Ada  Lovelace", created.Name);

        var updated = await ReadAsync<ContactResponse>(await Client.PutAsJsonAsync(
            $"/api/contacts/{created.Id}",
            new UpdateContactRequest("  Zoë  Smith  ", "ada@example.test", "Ref-1"), Json));
        var fetched = await ReadAsync<ContactResponse>(await Client.GetAsync($"/api/contacts/{created.Id}"));

        Assert.Equal("Zoë  Smith", updated.Name);
        Assert.Equal(updated.Id, fetched.Id);
        Assert.Equal(updated.Name, fetched.Name);
        Assert.Equal("Ref-1", fetched.ExternalId);
        Assert.Equal("ada@example.test", fetched.Email);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(201)]
    public async Task US05_InvalidNamesDoNotCreateOrChangeAContact(int length)
    {
        var workspace = await CreateWorkspaceAsync();
        var original = await CreateContactAsync(workspace.Id, "Original");
        var name = length switch
        {
            1 => "   ",
            201 => $" {new string('x', 199)} ", // Raw length fails even though trimming would fit.
            _ => new string('x', length),
        };
        var create = await Client.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/contacts",
            new CreateContactRequest(name, null, null), Json);
        var update = await Client.PutAsJsonAsync($"/api/contacts/{original.Id}",
            new UpdateContactRequest(name, null, null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var rows = await ReadAsync<List<ContactResponse>>(await Client.GetAsync(
            $"/api/workspaces/{workspace.Id}/contacts"));
        Assert.Equal("Original", Assert.Single(rows).Name);
    }
}
