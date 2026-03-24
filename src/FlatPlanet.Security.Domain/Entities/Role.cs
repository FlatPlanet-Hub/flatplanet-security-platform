namespace FlatPlanet.Security.Domain.Entities;

public class Role
{
    public Guid Id { get; set; }
    public Guid? AppId { get; set; }       // null for platform-level roles
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPlatformRole { get; set; }
    public DateTime CreatedAt { get; set; }
}
