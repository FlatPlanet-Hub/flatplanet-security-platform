using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Application.Services;

public class AuthService : IAuthService
{
    private readonly ILoginService _login;
    private readonly IPasswordService _password;
    private readonly IProfileService _profile;

    public AuthService(ILoginService login, IPasswordService password, IProfileService profile)
    {
        _login    = login;
        _password = password;
        _profile  = profile;
    }

    public Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent) =>
        _login.LoginAsync(request, ipAddress, userAgent);

    public Task LogoutAsync(Guid? sessionId, Guid userId, string? ipAddress) =>
        _login.LogoutAsync(sessionId, userId, ipAddress);

    public Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress) =>
        _login.RefreshAsync(request, ipAddress);

    public Task<UserProfileResponse> GetProfileAsync(Guid userId, string? appSlug) =>
        _profile.GetProfileAsync(userId, appSlug);

    public Task<UpdateProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, string? ipAddress) =>
        _profile.UpdateProfileAsync(userId, request, ipAddress);

    public Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ipAddress) =>
        _password.ChangePasswordAsync(userId, request, ipAddress);

    public Task ForgotPasswordAsync(ForgotPasswordRequest request) =>
        _password.ForgotPasswordAsync(request);

    public Task AdminForceResetPasswordAsync(Guid userId, Guid performedByUserId) =>
        _password.AdminForceResetPasswordAsync(userId, performedByUserId);

    public Task ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress) =>
        _password.ResetPasswordAsync(request, ipAddress);
}
