namespace FlatPlanet.Security.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneVerified { get; set; }
    public bool MfaEnabled { get; set; }
    public string? MfaMethod { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}
