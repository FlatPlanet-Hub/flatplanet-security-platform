using System.ComponentModel.DataAnnotations;

namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateUserRequest
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? RoleTitle { get; set; }

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

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
