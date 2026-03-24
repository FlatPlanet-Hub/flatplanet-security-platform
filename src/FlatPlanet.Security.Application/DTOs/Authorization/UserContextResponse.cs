namespace FlatPlanet.Security.Application.DTOs.Authorization;

public class UserContextResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<AllowedAppDto> AllowedApps { get; set; } = new();
}

public class AllowedAppDto
{
    public Guid AppId { get; set; }
    public string AppSlug { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
}
