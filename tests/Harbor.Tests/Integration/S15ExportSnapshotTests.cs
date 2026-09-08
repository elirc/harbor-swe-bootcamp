using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harbor.Api.Contracts;
using Harbor.Api.Controllers;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tests.Integration;

public class S15ExportSnapshotTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }

    [Fact]
    public async Task MultiplePagesRetainOneSnapshotWhileAnotherConnectionCommitsChanges()
    {
        var path = Path.Combine(Path.GetTempPath(), "harbor-export-snapshot-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var factory = new HarborApiFactory { FileDatabasePath = path };
            using var client = factory.CreateClient();
            var ws = await Read<CreateWorkspaceResponse>(await client.PostAsJsonAsync("/api/workspaces", new CreateWorkspaceRequest("Snapshot", "Owner", "owner@snapshot.test"), Json));
            client.DefaultRequestHeaders.Add("X-Api-Key", ws.ApiKey);
            var inbox = await Read<InboxResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws.Workspace.Id}/inboxes", new CreateInboxRequest("Support", null), Json));
            var contact = await Read<ContactResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws.Workspace.Id}/contacts", new CreateContactRequest("Ada", null, null), Json));
            Guid last = default;
            factory.WithDb(db =>
            {
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                var started = DateTimeOffset.UtcNow;
                var rows = Enumerable.Range(0, 251).Select(i => new Conversation { WorkspaceId = ws.Workspace.Id, InboxId = inbox.Id, ContactId = contact.Id,
                    Subject = "Original " + i, CreatedAt = started.AddSeconds(i), UpdatedAt = started, LastMessageAt = started }).ToList();
                last = rows[^1].Id; db.Conversations.AddRange(rows); db.SaveChanges();
            });
            var job = await Read<ConversationExportResponse>(await client.PostAsJsonAsync($"/api/workspaces/{ws.Workspace.Id}/conversation-exports", new { }, Json));
            using var scope = factory.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<ConversationExports>();
            processor.AfterSnapshotEstablished = () =>
            {
                factory.WithDb(db => { var row = db.Conversations.Single(c => c.Id == last); row.Subject = "Changed after snapshot";
                    db.Conversations.Add(new Conversation { WorkspaceId = ws.Workspace.Id, InboxId = inbox.Id, ContactId = contact.Id, Subject = "Late arrival" }); db.SaveChanges(); });
                return Task.CompletedTask;
            };
            Assert.True(await processor.ProcessOneAsync());
            var content = await (await client.GetAsync($"/api/conversation-exports/{job.Id}/download")).Content.ReadAsStringAsync();
            Assert.Contains("Original 250", content); Assert.DoesNotContain("Changed after snapshot", content); Assert.DoesNotContain("Late arrival", content);
            Assert.Equal(252, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            factory.WithDb(db => Assert.Equal("Changed after snapshot", db.Conversations.Single(c => c.Id == last).Subject));
        }
        finally { foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }
}
