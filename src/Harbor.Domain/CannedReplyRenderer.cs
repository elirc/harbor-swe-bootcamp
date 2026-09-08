using System.Text;

namespace Harbor.Domain;

public record CannedReplyValues(string ContactName, string? ContactEmail, string TeammateName, string? ConversationSubject);
public record CannedReplyRenderResult(string Body, IReadOnlyList<string> UsedTokens);

public static class CannedReplyRenderer
{
    public const int MaxLength = 20_000;

    public static CannedReplyRenderResult Render(string template, CannedReplyValues context)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["contact.name"] = context.ContactName, ["contact.email"] = context.ContactEmail,
            ["teammate.name"] = context.TeammateName, ["conversation.subject"] = context.ConversationSubject,
        };
        var used = new SortedSet<string>(StringComparer.Ordinal);
        var result = new StringBuilder(Math.Min(template.Length, MaxLength));
        for (var position = 0; position < template.Length;)
        {
            if (template.AsSpan(position).StartsWith("}}")) throw new DomainException("Unmatched closing template delimiter.");
            if (!template.AsSpan(position).StartsWith("{{"))
            {
                if (result.Length == MaxLength) throw new DomainException("Rendered reply exceeds 20000 characters.");
                result.Append(template[position++]);
                continue;
            }
            var end = template.IndexOf("}}", position + 2, StringComparison.Ordinal);
            if (end < 0) throw new DomainException("Unmatched opening template delimiter.");
            var token = template[(position + 2)..end].Trim();
            if (token.Contains('{') || token.Contains('}')) throw new DomainException("Nested template delimiters are not supported.");
            if (!values.TryGetValue(token, out var value)) throw new DomainException($"Unknown canned-reply token '{token}'.");
            value ??= "";
            if (value.Length > MaxLength - result.Length) throw new DomainException("Rendered reply exceeds 20000 characters.");
            // Append customer data literally; only the original template is scanned.
            result.Append(value);
            used.Add(token);
            position = end + 2;
        }
        return new CannedReplyRenderResult(result.ToString(), used.ToArray());
    }
}
