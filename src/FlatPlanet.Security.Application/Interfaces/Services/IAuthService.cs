using FlatPlanet.Security.Application.DTOs.Auth;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent);
    Task LogoutAsync(Guid? sessionId, Guid userId, string? ipAddress);
    Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress);
    Task<UserProfileResponse> GetProfileAsync(Guid userId, string? appSlug);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ipAddress);
    Task ForgotPasswordAsync(ForgotPasswordRequest request);
    Task AdminForceResetPasswordAsync(Guid userId, string appSlug, Guid performedByUserId);
    Task ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress);
}
