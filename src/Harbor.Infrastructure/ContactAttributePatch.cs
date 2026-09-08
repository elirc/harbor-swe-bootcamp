namespace Harbor.Infrastructure;

public static class ContactAttributePatch
{
    public static (string? Json, string? Error) Apply(string original, IReadOnlyDictionary<string, string?> changes)
    {
        if (changes.Count == 0) return (original, null);
        if (changes.Count > 50) return (null, "At most 50 attribute changes are allowed.");
        var normalized = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (rawKey, value) in changes)
        {
            var key = rawKey.Trim();
            if (key.Length is 0 or > 100) return (null, "Attribute keys must contain 1-100 characters after trimming.");
            if (value?.Length > 2_000) return (null, "Attribute values cannot exceed 2000 characters.");
            if (!normalized.TryAdd(key, value)) return (null, "Attribute keys must be distinct after trimming.");
        }
        var result = new Dictionary<string, string?>(ContactAttributes.Read(original), StringComparer.Ordinal);
        foreach (var (key, value) in normalized)
            if (value is null) result.Remove(key); else result[key] = value;
        var json = ContactAttributes.Write(result);
        return json.Length > 10_000 ? (null, "The resulting attributes exceed 10000 serialized characters.") : (json, null);
    }
}
