using FlatPlanet.Security.Application.DTOs.Auth;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ILoginService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent);
    Task LogoutAsync(Guid? sessionId, Guid userId, string? ipAddress);
    Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress);
}
