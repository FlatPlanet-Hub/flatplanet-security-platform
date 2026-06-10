namespace FlatPlanet.Security.Domain.Entities;

/// <summary>
/// Per-service authentication token used by trusted backends to call SP.
/// Plaintext is shown to the admin exactly once at mint time; only the hash is stored.
/// </summary>
public class ServiceToken
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public string? Description { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Wildcard scope that satisfies any RequireScope check.</summary>
    public const string BootstrapScope = "bootstrap";

    public bool HasScope(string requiredScope) =>
        Status == "active" &&
        (Scopes.Contains(BootstrapScope, StringComparer.OrdinalIgnoreCase) ||
         Scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase));
}
