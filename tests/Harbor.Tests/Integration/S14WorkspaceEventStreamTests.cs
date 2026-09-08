using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tests.Integration;

public class S14WorkspaceEventStreamTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private sealed record Frame(string Kind, string? Cursor, JsonElement Data);
    private static async Task<Frame> ReadFrame(StreamReader reader, CancellationToken ct)
    {
        string? kind = null, id = null, data = null;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event: ")) kind = line[7..];
            if (line.StartsWith("id: ")) id = line[4..];
            if (line.StartsWith("data: ")) data = line[6..];
            if (line.Length == 0 && kind is not null) return new(kind, id, JsonSerializer.Deserialize<JsonElement>(data!));
        }
        throw new InvalidOperationException("Stream ended before a complete frame.");
    }

    [Fact]
    public async Task ReplayUsesSequenceAndExcludesInterleavedForeignEventsAndBodies()
    {
        var a = await CreateWorkspaceAsync(); var ai = await CreateInboxAsync(a.Id); var ac = await CreateContactAsync(a.Id);
        var first = await StartConversationAsync(a.Id, ai.Id, ac.Id);
        var b = await CreateWorkspaceAsync(); var bi = await CreateInboxAsync(b.Id); var bc = await CreateContactAsync(b.Id);
        await StartConversationAsync(b.Id, bi.Id, bc.Id);
        ActAsAdminOf(a.Id);
        var second = await StartConversationAsync(a.Id, ai.Id, ac.Id);
        var cursors = Factory.Services.GetRequiredService<EventCursors>();
        long after = 0;
        Factory.WithDb(db => after = db.WorkspaceEvents.Where(e => e.ConversationId == first.Id).Max(e => e.Sequence));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{a.Id}/events");
        request.Headers.Add("Last-Event-ID", cursors.Issue(a.Id, after));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
        response.EnsureSuccessStatusCode(); Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel.Token));
        Assert.Equal("ready", (await ReadFrame(reader, cancel.Token)).Kind);
        var created = await ReadFrame(reader, cancel.Token); var message = await ReadFrame(reader, cancel.Token);
        Assert.Equal("ConversationCreated", created.Kind); Assert.Equal("MessageCreated", message.Kind);
        Assert.Equal(second.Id, created.Data.GetProperty("conversationId").GetGuid());
        Assert.True(message.Data.GetProperty("sequence").GetInt64() > created.Data.GetProperty("sequence").GetInt64());
        Assert.Equal(200, cursors.Read(a.Id, message.Cursor!).Status);
        Assert.DoesNotContain("Body", message.Data.GetRawText(), StringComparison.OrdinalIgnoreCase);
        cancel.Cancel();
    }

    [Fact]
    public async Task PruneStopsAtRetainedPrefixAndStaleCursorsGetRefreshInstruction()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        await StartConversationAsync(ws.Id, inbox.Id, contact.Id); await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        long first = 0;
        Factory.WithDb(db =>
        {
            var rows = db.WorkspaceEvents.Where(e => e.WorkspaceId == ws.Id).OrderBy(e => e.Sequence).ToList();
            first = rows[0].Sequence;
            rows[0].RecordedAt = DateTimeOffset.UtcNow.AddDays(-8);
            rows[2].RecordedAt = DateTimeOffset.UtcNow.AddDays(-8);
            db.SaveChanges();
        });
        (await Client.PostAsync($"/api/workspaces/{ws.Id}/events/prune", null)).EnsureSuccessStatusCode();
        Factory.WithDb(db =>
        {
            Assert.Equal(first, db.WorkspaceEventWatermarks.Single(w => w.WorkspaceId == ws.Id).PurgedThrough);
            Assert.Equal(3, db.WorkspaceEvents.Count(e => e.WorkspaceId == ws.Id));
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{ws.Id}/events");
        request.Headers.Add("Last-Event-ID", Factory.Services.GetRequiredService<EventCursors>().Issue(ws.Id, 0));
        Assert.Equal(HttpStatusCode.Gone, (await Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task FailedMessageCannotLeaveAStreamEvent()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        Factory.FailCommand = command => command.CommandText.Contains("INSERT INTO \"WorkspaceEvents\"");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new AddMessageRequest(AuthorType.Contact, contact.Id, MessageKind.Reply, "Uncommitted body"), Json)).StatusCode); }
        finally { Factory.FailCommand = null; }
        Factory.WithDb(db => { Assert.Equal(2, db.WorkspaceEvents.Count(e => e.ConversationId == conversation.Id)); Assert.Single(db.Messages.Where(m => m.ConversationId == conversation.Id)); });
    }
}
