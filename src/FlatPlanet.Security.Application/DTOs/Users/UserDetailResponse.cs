using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.DTOs.Users;

public class UserDetailResponse : UserResponse
{
    public IEnumerable<UserAppAccessDto> AppAccess { get; set; } = [];
}

public class UserAppAccessDto
{
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string AppSlug { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class UserAppRoleDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string AppSlug { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
