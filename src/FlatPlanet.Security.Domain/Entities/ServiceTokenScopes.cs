using System.Collections.Generic;

namespace FlatPlanet.Security.Domain.Entities;

/// <summary>
/// Canonical scope strings used by [RequireScope(...)] on admin endpoints.
/// Token-minting flows and trusted backends (e.g. hubapi) should reference
/// these constants instead of hard-coding strings.
/// </summary>
public static class ServiceTokenScopes
{
    public const string UsersRead = "users:read";
    public const string UsersWrite = "users:write";
    /// <summary>MFA admin operations (disable, reset, set-method) — higher privilege than users:write.</summary>
    public const string UsersMfa = "users:mfa";

    public const string RolesRead = "roles:read";
    public const string RolesWrite = "roles:write";

    public const string PermissionsRead = "permissions:read";
    public const string PermissionsWrite = "permissions:write";

    public const string ResourcesRead = "resources:read";
    public const string ResourcesWrite = "resources:write";

    public const string GrantsRead = "grants:read";
    public const string GrantsWrite = "grants:write";

    public const string AppsRead = "apps:read";
    public const string AppsWrite = "apps:write";
    public const string AppsAdmin = "apps:admin";

    public const string AuditRead = "audit:read";

    /// <summary>Irreversible compliance operations (anonymize). Separate from users:write intentionally.</summary>
    public const string ComplianceWrite = "compliance:write";

    public static readonly IReadOnlyList<string> All = new[]
    {
        UsersRead, UsersWrite, UsersMfa,
        RolesRead, RolesWrite,
        PermissionsRead, PermissionsWrite,
        ResourcesRead, ResourcesWrite,
        GrantsRead, GrantsWrite,
        AppsRead, AppsWrite, AppsAdmin,
        AuditRead,
        ComplianceWrite,
    };

    public static bool IsKnown(string scope) =>
        scope == ServiceToken.BootstrapScope ||
        All.Any(s => string.Equals(s, scope, StringComparison.OrdinalIgnoreCase));
}
