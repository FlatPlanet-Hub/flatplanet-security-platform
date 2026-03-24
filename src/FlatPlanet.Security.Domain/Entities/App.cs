namespace FlatPlanet.Security.Domain.Entities;

public class App
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime RegisteredAt { get; set; }
    public Guid RegisteredBy { get; set; }
}
