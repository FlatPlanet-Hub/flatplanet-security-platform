namespace FlatPlanet.Security.Application.DTOs.Admin;

public class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
}

public class UpdateUserStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class UserResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
