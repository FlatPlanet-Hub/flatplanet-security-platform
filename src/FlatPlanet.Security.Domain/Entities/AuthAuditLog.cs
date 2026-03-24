namespace FlatPlanet.Security.Domain.Entities;

public class AuthAuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? AppId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }   // JSONB stored as string
    public DateTime CreatedAt { get; set; }
}
