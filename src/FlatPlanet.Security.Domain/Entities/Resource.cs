namespace FlatPlanet.Security.Domain.Entities;

public class Resource
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid ResourceTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
}
