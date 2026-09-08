using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Tests.Integration;

public class SubjectEditingLearningTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task M04_EditNoOpClearAndStaleConversationAreDistinct()
    {
        var workspace = await CreateWorkspaceAsync();
        var inbox = await CreateInboxAsync(workspace.Id, slaMinutes: 30);
        var contact = await CreateContactAsync(workspace.Id);
        var conversation = await StartConversationAsync(workspace.Id, inbox.Id, contact.Id);
        var route = $"/api/conversations/{conversation.Id}/subject";
        var before = await ReadAsync<SubjectResponse>(await Client.GetAsync(route));
        var edited = await ReadAsync<SubjectResponse>(await Client.PutAsJsonAsync(route,
            new UpdateSubjectRequest("  Refund  question  ", before.Version)));
        Assert.Equal("Refund  question", edited.Subject);
        Assert.NotEqual(before.Version, edited.Version);
        Assert.Equal(edited, await ReadAsync<SubjectResponse>(await Client.PutAsJsonAsync(route,
            new UpdateSubjectRequest("Refund  question", edited.Version))));
        var detail = await ReadAsync<ConversationDetailResponse>(await Client.GetAsync($"/api/conversations/{conversation.Id}"));
        Assert.Equal(conversation.LastMessageAt, detail.LastMessageAt);
        Assert.Equal(conversation.FirstResponseDueAt, detail.FirstResponseDueAt);
        Assert.Equal(conversation.State, detail.State);
        Assert.Single(detail.Messages);
        await SetPriorityAsync(conversation.Id, ConversationPriority.High);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(route,
            new UpdateSubjectRequest("Stale edit", edited.Version))).StatusCode);
        var current = await ReadAsync<SubjectResponse>(await Client.GetAsync(route));
        Assert.Equal(edited.Subject, current.Subject);
        var cleared = await ReadAsync<SubjectResponse>(await Client.PutAsJsonAsync(route,
            new UpdateSubjectRequest("  ", current.Version)));
        Assert.Null(cleared.Subject);
        foreach (var body in new object[] { new { subject = "Missing token" },
            new UpdateSubjectRequest("Empty token", Guid.Empty), new UpdateSubjectRequest(new string('x', 501), cleared.Version) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync(route, body)).StatusCode);
        await CreateWorkspaceAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync(route, new UpdateSubjectRequest("Foreign", cleared.Version))).StatusCode);
    }
}
