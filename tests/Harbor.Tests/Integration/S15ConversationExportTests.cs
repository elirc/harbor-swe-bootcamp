using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tests.Integration;

public class S15ConversationExportTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private async Task<ConversationExportResponse> Enqueue(Guid ws, object filters) => await ReadAsync<ConversationExportResponse>(
        await Client.PostAsJsonAsync($"/api/workspaces/{ws}/conversation-exports", filters, Json));
    private async Task Process()
    { using var scope = Factory.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<ConversationExports>().ProcessOneAsync()); }

    [Fact]
    public async Task CompletedFileHasManifestOrderedSummariesAndExpiresAsAWhole()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var a = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var b = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var c = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var job = await Enqueue(ws.Id, new { inboxId = inbox.Id });
        Assert.Equal(ConversationExportStatus.Queued, job.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.GetAsync($"/api/conversation-exports/{job.Id}/download")).StatusCode);
        await Process();
        var ready = await ReadAsync<ConversationExportResponse>(await Client.GetAsync($"/api/conversation-exports/{job.Id}"));
        Assert.Equal(ConversationExportStatus.Ready, ready.Status); Assert.Equal(3, ready.RowCount);
        var content = await (await Client.GetAsync($"/api/conversation-exports/{job.Id}/download")).Content.ReadAsStringAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonSerializer.Deserialize<JsonElement>(s)).ToArray();
        Assert.Equal(4, lines.Length); Assert.Equal("manifest", lines[0].GetProperty("type").GetString());
        Assert.Equal(3, lines[0].GetProperty("rowCount").GetInt32());
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, lines.Skip(1).Select(l => l.GetProperty("id").GetGuid()));
        Assert.All(lines.Skip(1), l => { Assert.False(l.TryGetProperty("messages", out _)); Assert.False(l.TryGetProperty("body", out _)); });
        Factory.FrozenTime = ready.ExpiresAt;
        try
        {
            Assert.Equal(HttpStatusCode.Gone, (await Client.GetAsync($"/api/conversation-exports/{job.Id}/download")).StatusCode);
            (await Client.PostAsync($"/api/workspaces/{ws.Id}/conversation-exports/reconcile", null)).EnsureSuccessStatusCode();
            Assert.Empty(Factory.Services.GetRequiredService<PrivateFiles>().Artifacts(ws.Id, ArtifactKind.Exports));
        }
        finally { Factory.FrozenTime = null; }
    }

    [Fact]
    public async Task CapturedSegmentRulesSurviveEditsAndPagingIsRejected()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id);
        var contact = await CreateContactAsync(ws.Id, "Ada", "ada@enterprise.test");
        await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var rules = new SegmentRuleSet(SegmentMatch.All, [new SegmentCondition("email", SegmentOperator.EndsWith, "@enterprise.test")]);
        var segment = await ReadAsync<SegmentResponse>(await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/segments", new CreateSegmentRequest("Enterprise", rules), Json));
        var job = await Enqueue(ws.Id, new { segmentId = segment.Id });
        Factory.WithDb(db => { db.Segments.Single(s => s.Id == segment.Id).RulesJson = JsonSerializer.Serialize(new SegmentRuleSet(SegmentMatch.All, [new SegmentCondition("email", SegmentOperator.EndsWith, "@other.test")]), Json); db.SaveChanges(); });
        await Process();
        Assert.Equal(1, (await ReadAsync<ConversationExportResponse>(await Client.GetAsync($"/api/conversation-exports/{job.Id}"))).RowCount);
        var file = await (await Client.GetAsync($"/api/conversation-exports/{job.Id}/download")).Content.ReadAsStringAsync();
        var manifest = JsonSerializer.Deserialize<JsonElement>(file.Split('\n')[0]);
        Assert.Contains("@enterprise.test", manifest.GetProperty("capturedSegmentRules").GetRawText());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/conversation-exports", new { page = 2 }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/conversation-exports", new { unsupported = "value" }, Json)).StatusCode);
    }

    [Fact]
    public async Task ExpiredWorkerCannotPublishAndCrashAttemptsAreBounded()
    {
        var ws = await CreateWorkspaceAsync(); var job = await Enqueue(ws.Id, new { });
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ConversationExports>();
        var now = DateTimeOffset.UtcNow; Factory.FrozenTime = now;
        try
        {
            var first = (await service.ClaimAsync())!;
            Assert.Null(await service.ClaimAsync());
            Factory.FrozenTime = now.AddMinutes(5);
            var second = (await service.ClaimAsync())!;
            Assert.NotEqual(first.LeaseToken, second.LeaseToken);
            Assert.False(await service.CompleteAsync(first, new StoredArtifact(Guid.NewGuid(), 1, new string('a', 64)), now, 0));
            Factory.FrozenTime = now.AddMinutes(10);
            Assert.Null(await service.ClaimAsync());
            var failed = await ReadAsync<ConversationExportResponse>(await Client.GetAsync($"/api/conversation-exports/{job.Id}"));
            Assert.Equal(ConversationExportStatus.Failed, failed.Status); Assert.Equal(2, failed.AttemptCount);
            Assert.Equal("AttemptsExhausted", failed.FailureCode);
        }
        finally { Factory.FrozenTime = null; }
    }

    [Fact]
    public async Task RowLimitFailsBeforePublishingAnArtifact()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        Factory.WithDb(db =>
        {
            db.Conversations.AddRange(Enumerable.Range(0, 10_001).Select(_ => new Conversation { WorkspaceId = ws.Id, InboxId = inbox.Id, ContactId = contact.Id }));
            db.SaveChanges();
        });
        var job = await Enqueue(ws.Id, new { }); await Process();
        var failed = await ReadAsync<ConversationExportResponse>(await Client.GetAsync($"/api/conversation-exports/{job.Id}"));
        Assert.Equal("RowLimitExceeded", failed.FailureCode);
        Assert.Empty(Factory.Services.GetRequiredService<PrivateFiles>().Artifacts(ws.Id, ArtifactKind.Exports));
    }

    [Fact]
    public async Task RepeatedCompletionKeepsTheAlreadyPublishedArtifact()
    {
        var ws = await CreateWorkspaceAsync(); var job = await Enqueue(ws.Id, new { });
        using var scope = Factory.Services.CreateScope(); var processor = scope.ServiceProvider.GetRequiredService<ConversationExports>();
        var claim = (await processor.ClaimAsync())!;
        var files = Factory.Services.GetRequiredService<PrivateFiles>();
        var artifact = await files.StoreAsync(ws.Id, ArtifactKind.Exports, new MemoryStream("{}\n"u8.ToArray()), 100, default);
        Assert.True(await processor.CompleteAsync(claim, artifact, DateTimeOffset.UtcNow, 0));
        Assert.True(await processor.CompleteAsync(claim, artifact, DateTimeOffset.UtcNow, 0));
        Assert.True(files.Exists(ws.Id, ArtifactKind.Exports, artifact.StorageId));
        Assert.Equal(ConversationExportStatus.Ready, (await ReadAsync<ConversationExportResponse>(await Client.GetAsync($"/api/conversation-exports/{job.Id}"))).Status);
    }
}
