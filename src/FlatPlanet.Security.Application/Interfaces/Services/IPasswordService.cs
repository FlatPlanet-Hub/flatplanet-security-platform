using FlatPlanet.Security.Application.DTOs.Auth;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IPasswordService
{
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ipAddress);
    Task ForgotPasswordAsync(ForgotPasswordRequest request);
    Task AdminForceResetPasswordAsync(Guid userId, Guid performedByUserId);
    Task ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress);
}
