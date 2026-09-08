using System.Security.Cryptography;
using System.Text;
using Harbor.Domain.Entities;

namespace Harbor.Infrastructure;

/// <summary>
/// Teammate API keys: generated once, shown once, and stored only as a
/// SHA-256 hex digest.
/// </summary>
public static class ApiKeys
{
    /// <summary>Stage the compatibility credential with its new teammate; never call this to repair a revoked key.</summary>
    public static void StageInitial(HarborDbContext db, Teammate teammate) => db.TeammateApiKeys.Add(new TeammateApiKey
    {
        Id = teammate.Id, WorkspaceId = teammate.WorkspaceId, TeammateId = teammate.Id,
        Name = "Initial key", Hash = teammate.ApiKeyHash, CreatedAt = teammate.CreatedAt,
    });

    /// <summary>Creates a new random API key ("hbk_" + 48 hex chars).</summary>
    public static string Generate() =>
        $"hbk_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24))}";

    /// <summary>Hex-encoded SHA-256 digest of the raw key, persisted on a credential.</summary>
    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
