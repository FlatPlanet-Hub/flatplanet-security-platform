using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.ServiceTokens;

public class MintServiceTokenRequest
{
    [Required]
    [RegularExpression(@"^[a-z][a-z0-9-]{1,49}$",
        ErrorMessage = "serviceName must be 2-50 chars, lowercase, alphanumeric + hyphens, starting with a letter.")]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Scopes granted to the token. Pass ["bootstrap"] for full access during onboarding.</summary>
    public string[] Scopes { get; set; } = [];

    [MaxLength(500)]
    public string? Description { get; set; }
}

public class MintServiceTokenResponse
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public string? Description { get; set; }

    /// <summary>
    /// Plaintext token. Shown ONCE — never retrievable again. Caller must record it.
    /// Format: fps_&lt;service&gt;_&lt;43-char base64url&gt;
    /// </summary>
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public class ServiceTokenResponse
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class UpdateScopesRequest
{
    [Required]
    public string[] Scopes { get; set; } = [];
}

public class UnknownScopeAuditEntry
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string[] UnknownScopes { get; set; } = [];
}
