using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Harbor.Api.Infrastructure;

public sealed record EventCursor(int Version, Guid WorkspaceId, long Sequence, DateTimeOffset ExpiresAt);
public sealed class EventCursors(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Harbor.WorkspaceEvents.Resume.v1");
    public string Issue(Guid workspace, long sequence) => protector.Protect(JsonSerializer.Serialize(new EventCursor(1, workspace, sequence, clock.GetUtcNow().AddDays(7))));
    public (int Status, EventCursor? Cursor) Read(Guid workspace, string token)
    {
        if (token.Length is < 1 or > 4096) return (400, null);
        try
        {
            var cursor = JsonSerializer.Deserialize<EventCursor>(protector.Unprotect(token));
            if (cursor is null || cursor.Version != 1 || cursor.WorkspaceId != workspace || cursor.Sequence < 0) return (400, null);
            if (cursor.ExpiresAt <= clock.GetUtcNow()) return (410, null);
            return (200, cursor);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return (400, null); }
    }
}
