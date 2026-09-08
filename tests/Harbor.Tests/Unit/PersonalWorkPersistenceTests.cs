using Harbor.Domain.Entities;
using Harbor.Domain;
using Harbor.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Tests.Unit;

/// <summary>Each context opens its own connection; the anchor keeps the named memory database alive.</summary>
public class PersonalWorkPersistenceTests : IDisposable
{
    private readonly SqliteConnection anchor;
    private readonly DbContextOptions<HarborDbContext> options;
    public PersonalWorkPersistenceTests()
    {
        var connection = $"Data Source=personal-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        anchor = new SqliteConnection(connection);
        anchor.Open();
        options = new DbContextOptionsBuilder<HarborDbContext>().UseSqlite(connection).Options;
    }

    private (Guid Workspace, Guid Owner, Guid Conversation) Seed(bool migrate = false)
    {
        using var db = new HarborDbContext(options);
        if (migrate) db.Database.Migrate(); else db.Database.EnsureCreated();
        DataSeeder.Seed(db);
        return (db.Workspaces.First().Id, db.Teammates.First().Id, db.Conversations.First().Id);
    }

    [Fact]
    public void M03_OverlappingDraftEditsAndDeletesRejectTheStaleDatabaseToken()
    {
        var ids = Seed();
        using (var setup = new HarborDbContext(options))
        {
            setup.ReplyDrafts.Add(new ReplyDraft { WorkspaceId = ids.Workspace, OwnerTeammateId = ids.Owner,
                ConversationId = ids.Conversation, Body = "V1" });
            setup.SaveChanges();
        }
        using var alice = new HarborDbContext(options);
        using var bob = new HarborDbContext(options);
        var a = alice.ReplyDrafts.Single();
        var b = bob.ReplyDrafts.Single();
        a.Body = "Alice's V2";
        alice.SaveChanges();
        b.Body = "Bob's stale replacement";
        Assert.Throws<DbUpdateConcurrencyException>(() => bob.SaveChanges());
        bob.ReplyDrafts.Remove(b);
        Assert.Throws<DbUpdateConcurrencyException>(() => bob.SaveChanges());
        using var read = new HarborDbContext(options);
        Assert.Equal("Alice's V2", read.ReplyDrafts.Single().Body);
    }

    [Fact]
    public void M02_M03_DatabaseRejectsCompetingDuplicateRelationships()
    {
        var ids = Seed();
        using var a = new HarborDbContext(options);
        using var b = new HarborDbContext(options);
        a.ConversationBookmarks.Add(new ConversationBookmark { WorkspaceId = ids.Workspace, TeammateId = ids.Owner, ConversationId = ids.Conversation });
        b.ConversationBookmarks.Add(new ConversationBookmark { WorkspaceId = ids.Workspace, TeammateId = ids.Owner, ConversationId = ids.Conversation });
        a.SaveChanges();
        var duplicate = Assert.Throws<DbUpdateException>(() => b.SaveChanges());
        Assert.Equal(1555, Assert.IsType<SqliteException>(duplicate.InnerException).SqliteExtendedErrorCode);
        b.ChangeTracker.Clear();
        a.ReplyDrafts.Add(new ReplyDraft { WorkspaceId = ids.Workspace, OwnerTeammateId = ids.Owner, ConversationId = ids.Conversation, Body = "A" });
        b.ReplyDrafts.Add(new ReplyDraft { WorkspaceId = ids.Workspace, OwnerTeammateId = ids.Owner, ConversationId = ids.Conversation, Body = "B" });
        a.SaveChanges();
        duplicate = Assert.Throws<DbUpdateException>(() => b.SaveChanges());
        Assert.Equal(2067, Assert.IsType<SqliteException>(duplicate.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public void M01_M02_M03_MigratedSchemaPersistsPersonalResourcesAcrossConnections()
    {
        var ids = Seed(migrate: true);
        using (var write = new HarborDbContext(options))
        {
            write.SavedQueues.Add(new SavedQueue { WorkspaceId = ids.Workspace, OwnerTeammateId = ids.Owner, Name = "Open", FilterJson = "{\"state\":\"Open\"}" });
            write.ConversationBookmarks.Add(new ConversationBookmark { WorkspaceId = ids.Workspace, TeammateId = ids.Owner, ConversationId = ids.Conversation });
            write.ReplyDrafts.Add(new ReplyDraft { WorkspaceId = ids.Workspace, OwnerTeammateId = ids.Owner, ConversationId = ids.Conversation, Body = "Persist me" });
            write.SaveChanges();
        }
        using var read = new HarborDbContext(options);
        Assert.Equal("Open", read.SavedQueues.Single().Name);
        Assert.Equal(ids.Conversation, read.ConversationBookmarks.Single().ConversationId);
        Assert.Equal("Persist me", read.ReplyDrafts.Single().Body);
    }

    [Fact]
    public async Task M05_OneStaleConversationRollsBackTheWholePriorityBatch()
    {
        Seed();
        using var stale = new HarborDbContext(options);
        var rows = stale.Conversations.Include(c => c.Inbox).OrderBy(c => c.Id).Take(2).ToList();
        Assert.Equal(2, rows.Count);
        var firstPriority = rows[0].Priority;
        var firstDue = rows[0].FirstResponseDueAt;
        using (var other = new HarborDbContext(options))
        {
            var changed = other.Conversations.Single(c => c.Id == rows[1].Id);
            changed.ChangeSubject("Other request won", DateTimeOffset.UtcNow);
            other.SaveChanges();
        }
        foreach (var row in rows)
            await PriorityChanges.ApplyAsync(stale, row, row.Inbox!, ConversationPriority.Urgent, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        using var read = new HarborDbContext(options);
        var first = read.Conversations.Single(c => c.Id == rows[0].Id);
        Assert.Equal(firstPriority, first.Priority);
        Assert.Equal(firstDue, first.FirstResponseDueAt);
        Assert.Equal("Other request won", read.Conversations.Single(c => c.Id == rows[1].Id).Subject);
    }

    public void Dispose() => anchor.Dispose();
}
