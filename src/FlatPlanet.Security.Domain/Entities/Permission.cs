namespace FlatPlanet.Security.Domain.Entities;

public class Permission
{
    public Guid Id { get; set; }
    public Guid? AppId { get; set; }       // null for platform-level permissions
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
