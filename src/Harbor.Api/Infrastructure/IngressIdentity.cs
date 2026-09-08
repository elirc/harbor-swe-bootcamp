using System.Security.Cryptography;
using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;

namespace Harbor.Api.Infrastructure;

public static class IngressIdentity
{
    public static bool ValidKey(string key) => key.Length is >= 1 and <= 128
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public static string Address(string value) => value.Trim().ToLowerInvariant();
    public static string? Identifier(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string Creation(StartConversationRequest input) => Hash(new
    {
        Schema = 1, input.InboxId, input.ContactId, input.Subject, input.Body,
        Priority = input.Priority ?? ConversationPriority.Normal,
    });
    public static string Email(InboundEmailRequest input) => Hash(new
    {
        Schema = 1, From = Address(input.From), To = Address(input.To),
        FromName = string.IsNullOrWhiteSpace(input.FromName) ? Address(input.From) : input.FromName.Trim(),
        MessageId = Identifier(input.MessageId), InReplyTo = Identifier(input.InReplyTo),
        References = input.References?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray() ?? [],
        input.Subject, input.Body,
    });
}
