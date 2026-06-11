using FlatPlanet.Security.Application.DTOs.Auth;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IFederatedLoginService
{
    Task<LoginResponse> FederatedLoginAsync(FederatedLoginRequest request, string? ipAddress, string? userAgent);
}
