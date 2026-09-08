using Harbor.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Harbor.Tests.Unit;

public class ContactUpgradeTests
{
    [Fact]
    public async Task M11_ExistingContactsReceiveTokensAndLaterStaleWritesAreRejected()
    {
        var connection = $"Data Source=contact-upgrade-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        await using var anchor = new SqliteConnection(connection);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<HarborDbContext>().UseSqlite(connection).Options;
        var workspaceId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcTicks;
        await using (var old = new HarborDbContext(options))
        {
            await old.GetService<IMigrator>().MigrateAsync("20260908190228_M01M02M03PersonalWork");
            // Use the old schema's explicit columns; querying the new Contact model
            // before its migration would itself require the missing Version column.
            await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Workspaces (Id, Name, CreatedAt) VALUES ({workspaceId}, {"Upgrade workspace"}, {now})");
            await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Contacts (Id, WorkspaceId, Name, AttributesJson, CreatedAt) VALUES ({contactId}, {workspaceId}, {"Existing contact"}, {"{\"plan\":\"pro\"}"}, {now})");
            await old.Database.MigrateAsync();
        }
        await using var first = new HarborDbContext(options);
        await using var second = new HarborDbContext(options);
        var a = await first.Contacts.SingleAsync(c => c.Id == contactId);
        var b = await second.Contacts.SingleAsync(c => c.Id == contactId);
        Assert.NotEqual(Guid.Empty, a.Version);
        Assert.Equal("Existing contact", a.Name);
        var initial = a.Version;
        a.AttributesJson = ContactAttributePatch.Apply(a.AttributesJson,
            new Dictionary<string, string?> { ["plan"] = "enterprise" }).Json!;
        await first.SaveChangesAsync();
        Assert.NotEqual(initial, a.Version);
        b.AttributesJson = "{\"plan\":\"free\"}";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = new HarborDbContext(options);
        Assert.Equal("enterprise", ContactAttributes.Read((await read.Contacts.SingleAsync()).AttributesJson)["plan"]);
        Assert.Empty(await read.ConversationTransferEvents.ToListAsync());
    }
}
