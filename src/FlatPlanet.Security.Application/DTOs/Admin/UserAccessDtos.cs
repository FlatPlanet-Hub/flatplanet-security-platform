namespace FlatPlanet.Security.Application.DTOs.Admin;

public class GrantUserAccessRequest
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class UpdateUserRoleRequest
{
    public Guid RoleId { get; set; }
}

public class UserAccessResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Flat projection returned by the single-JOIN query in GetByAppIdWithDetailsAsync —
/// avoids the N+1 per-user and per-role lookups in GetByAppIdAsync.
/// </summary>
public class UserAccessJoinedRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public bool UserIsActive { get; set; }
    public string RoleName { get; set; } = string.Empty;
}
