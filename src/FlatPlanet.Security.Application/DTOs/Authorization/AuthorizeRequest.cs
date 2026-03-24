namespace FlatPlanet.Security.Application.DTOs.Authorization;

public class AuthorizeRequest
{
    public string AppSlug { get; set; } = string.Empty;
    public string ResourceIdentifier { get; set; } = string.Empty;
    public string RequiredPermission { get; set; } = string.Empty;
}
