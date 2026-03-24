namespace FlatPlanet.Security.Domain.Entities;

public class UserAppRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AppId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime GrantedAt { get; set; }
    public Guid GrantedBy { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = "active";
}
