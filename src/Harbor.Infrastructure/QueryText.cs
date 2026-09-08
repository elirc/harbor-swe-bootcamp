namespace Harbor.Infrastructure;

/// <summary>Shared text semantics for queries evaluated by SQLite.</summary>
public static class QueryText
{
    // Compute once from the running framework rather than maintaining a second
    // hand-written list that could drift from string.IsNullOrWhiteSpace.
    public static string WhitespaceCharacters { get; } = new(
        Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(value => (char)value)
            .Where(char.IsWhiteSpace)
            .ToArray());
}
