namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateResourceTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ResourceTypeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
