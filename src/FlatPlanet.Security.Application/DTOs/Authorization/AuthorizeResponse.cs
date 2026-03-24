namespace FlatPlanet.Security.Application.DTOs.Authorization;

public class AuthorizeResponse
{
    public bool Allowed { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}
