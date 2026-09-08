using Harbor.Api.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace Harbor.Tests.Unit;

public class EventCursorTests
{
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }

    [Fact]
    public void PersistedProtectionKeysAllowRestartButRejectTamperingOtherTenantsAndExpiry()
    {
        var root = Path.Combine(Path.GetTempPath(), "harbor-cursors-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new Clock(); var workspace = Guid.NewGuid();
            var first = new EventCursors(DataProtectionProvider.Create(new DirectoryInfo(root), b => b.SetApplicationName("Harbor")), clock);
            var token = first.Issue(workspace, 14);
            var restarted = new EventCursors(DataProtectionProvider.Create(new DirectoryInfo(root), b => b.SetApplicationName("Harbor")), clock);
            Assert.Equal(14, restarted.Read(workspace, token).Cursor!.Sequence);
            Assert.Equal(400, restarted.Read(Guid.NewGuid(), token).Status);
            Assert.Equal(400, restarted.Read(workspace, "invalid" + token).Status);
            clock.Now = clock.Now.AddDays(7);
            Assert.Equal(410, restarted.Read(workspace, token).Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
