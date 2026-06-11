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

    public static readonly string[] All =
    {
        UsersRead, UsersWrite,
        RolesRead, RolesWrite,
        PermissionsRead, PermissionsWrite,
        ResourcesRead, ResourcesWrite,
        GrantsRead, GrantsWrite,
        AppsRead, AppsWrite, AppsAdmin,
        AuditRead,
    };

    public static bool IsKnown(string scope) =>
        scope == ServiceToken.BootstrapScope ||
        Array.Exists(All, s => string.Equals(s, scope, StringComparison.OrdinalIgnoreCase));
}
