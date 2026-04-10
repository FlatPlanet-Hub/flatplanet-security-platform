namespace FlatPlanet.Security.Domain.Entities;

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string? Code { get; set; }
    public DateTime CreatedAt { get; set; }
}
