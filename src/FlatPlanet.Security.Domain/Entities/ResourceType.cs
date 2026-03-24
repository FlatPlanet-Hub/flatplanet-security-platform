namespace FlatPlanet.Security.Domain.Entities;

public class ResourceType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
