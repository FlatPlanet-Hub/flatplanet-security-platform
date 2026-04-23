using FlatPlanet.Security.Application.DTOs.Authorization;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IAccessAuthorizationService
{
    Task<AuthorizeResponse> AuthorizeAsync(Guid userId, AuthorizeRequest request, string? ipAddress, bool isPlatformOwner = false);
}
