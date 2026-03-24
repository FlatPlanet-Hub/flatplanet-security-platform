namespace FlatPlanet.Security.Domain.Entities;

public class User
{
    public Guid Id { get; set; }           // matches Supabase Auth uid
    public Guid CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
