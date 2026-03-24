namespace FlatPlanet.Security.Application.DTOs.Admin;

public class CreateResourceRequest
{
    public Guid ResourceTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
}

public class UpdateResourceRequest
{
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ResourceResponse
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid ResourceTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
