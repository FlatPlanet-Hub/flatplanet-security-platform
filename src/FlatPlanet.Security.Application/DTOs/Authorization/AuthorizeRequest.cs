namespace FlatPlanet.Security.Application.DTOs.Authorization;

// Fix 2: UserId is NOT accepted from the request body — set by controller from JWT claims
public class AuthorizeRequest
{
    public Guid UserId { get; set; }
    public string AppSlug { get; set; } = string.Empty;
    public string ResourceIdentifier { get; set; } = string.Empty;
    public string RequiredPermission { get; set; } = string.Empty;
}

public class AuthorizeRequestBody
{
    public string AppSlug { get; set; } = string.Empty;
    public string ResourceIdentifier { get; set; } = string.Empty;
    public string RequiredPermission { get; set; } = string.Empty;
}
