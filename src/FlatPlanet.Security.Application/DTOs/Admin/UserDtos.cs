using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Admin;

public class UpdateUserRequest
{
    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? RoleTitle { get; set; }
}

public class UpdateUserStatusRequest
{
    [Required]
    [RegularExpression("^(active|suspended|inactive)$")]
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
