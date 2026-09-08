using System.Security.Cryptography;
using System.Text.Json;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public record MergeAttributeConflict(string Key, string? SourceValue, string? TargetValue);
public record MergeFieldDecisions(string Name, string? Email, string? ExternalId,
    IReadOnlyDictionary<string, string?> Attributes, DateTimeOffset? LastSeenAt);
public record MergePreview(Guid SourceId, Guid TargetId, int ConversationCount, int AuthoredMessageCount,
    MergeFieldDecisions TargetFields, IReadOnlyList<MergeAttributeConflict> AttributeConflicts, string Fingerprint);

/// <summary>Read inside the caller's transaction. Preview and commit must use the same definition.</summary>
public static class ContactMergePreview
{
    public static async Task<MergePreview> BuildAsync(HarborDbContext db, Guid workspaceId, Guid sourceId, Guid targetId, CancellationToken ct)
    {
        if (sourceId == targetId) throw new DomainException("Source and target must be different contacts.");
        // Refuse new live dependencies until a maintainer explicitly defines their merge behavior.
        var unsupported = db.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys())
            .Any(f => f.PrincipalEntityType.ClrType == typeof(Contact)
                && f.DeclaringEntityType.ClrType != typeof(Conversation) && f.DeclaringEntityType.ClrType != typeof(Message));
        if (unsupported) throw new DomainException("A contact dependency requires an updated merge policy.");
        var contacts = await db.Contacts.AsNoTracking().Where(c => c.WorkspaceId == workspaceId && (c.Id == sourceId || c.Id == targetId)).ToListAsync(ct);
        var source = contacts.SingleOrDefault(c => c.Id == sourceId);
        var target = contacts.SingleOrDefault(c => c.Id == targetId);
        if (source is null || target is null) throw new DomainException("Both contacts must exist in this workspace.");
        if (string.IsNullOrWhiteSpace(source.Email) || string.IsNullOrWhiteSpace(target.Email)
            || !string.Equals(source.Email.Trim(), target.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Contacts must have the same nonblank normalized email.");
        var sourceExternal = string.IsNullOrWhiteSpace(source.ExternalId) ? null : source.ExternalId.Trim();
        var targetExternal = string.IsNullOrWhiteSpace(target.ExternalId) ? null : target.ExternalId.Trim();
        if (sourceExternal is not null && targetExternal is not null && sourceExternal != targetExternal)
            throw new DomainException("Conflicting external IDs cannot be merged.");
        if (await db.Conversations.CountAsync(c => c.ContactId == sourceId, ct) > 100
            || await db.Messages.CountAsync(m => m.AuthorContactId == sourceId, ct) > 1_000)
            throw new DomainException("Source exceeds 100 conversations or 1,000 authored messages.");
        var conversations = await db.Conversations.AsNoTracking().Where(c => c.ContactId == sourceId)
            .OrderBy(c => c.Id).Select(c => new { c.Id, c.WorkspaceId, c.ContactId, c.Version }).ToListAsync(ct);
        var messages = await db.Messages.AsNoTracking().Where(m => m.AuthorContactId == sourceId)
            .OrderBy(m => m.Id).Select(m => new { m.Id, m.ConversationId, m.AuthorContactId, WorkspaceId = m.Conversation!.WorkspaceId }).ToListAsync(ct);
        if (conversations.Any(c => c.WorkspaceId != workspaceId) || messages.Any(m => m.WorkspaceId != workspaceId))
            throw new DomainException("Source has an unsupported cross-workspace relationship.");
        var sourceAttributes = ReadStrict(source.AttributesJson);
        var targetAttributes = ReadStrict(target.AttributesJson);
        var conflicts = sourceAttributes.Where(p => targetAttributes.TryGetValue(p.Key, out var value) && value != p.Value)
            .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new MergeAttributeConflict(p.Key, p.Value, targetAttributes[p.Key])).ToList();
        var merged = new SortedDictionary<string, string?>(sourceAttributes, StringComparer.Ordinal);
        foreach (var pair in targetAttributes) merged[pair.Key] = pair.Value;
        var serialized = ContactAttributes.Write(merged);
        if (serialized.Length > 10_000) throw new DomainException("Merged attributes exceed 10,000 characters.");
        var seen = source.LastSeenAt is { } sourceSeen && (target.LastSeenAt is null || sourceSeen > target.LastSeenAt)
            ? source.LastSeenAt : target.LastSeenAt;
        var decisions = new MergeFieldDecisions(target.Name, target.Email, targetExternal is null ? sourceExternal : target.ExternalId, merged, seen);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1,
            Source = new { source.Id, source.Version, source.Name, source.Email, source.ExternalId, source.AttributesJson, source.CreatedAt, source.LastSeenAt },
            Target = new { target.Id, target.Version, target.Name, target.Email, target.ExternalId, target.AttributesJson, target.CreatedAt, target.LastSeenAt },
            Conversations = conversations, Messages = messages, Decisions = decisions,
        });
        return new MergePreview(sourceId, targetId, conversations.Count, messages.Count, decisions, conflicts,
            Convert.ToHexString(SHA256.HashData(canonical)));
    }

    private static Dictionary<string, string?> ReadStrict(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? throw new JsonException(); }
        catch (JsonException) { throw new DomainException("Unreadable stored attributes must be corrected before merging."); }
    }
}
