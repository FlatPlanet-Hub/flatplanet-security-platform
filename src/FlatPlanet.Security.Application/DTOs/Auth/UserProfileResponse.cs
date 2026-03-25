namespace FlatPlanet.Security.Application.DTOs.Auth;

public class UserProfileResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string CompanyId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastSeenAt { get; set; }
    public IEnumerable<string> PlatformRoles { get; set; } = [];
    public IEnumerable<AppAccessDto> AppAccess { get; set; } = [];
}

public class AppAccessDto
{
    public string AppSlug { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public IEnumerable<string> Permissions { get; set; } = [];
}
