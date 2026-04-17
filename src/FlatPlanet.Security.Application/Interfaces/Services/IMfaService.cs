using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IMfaService
{
    // TOTP enrolment
    Task<BeginTotpEnrolmentResponse> BeginTotpEnrolmentAsync(Guid userId);
    Task<LoginResponse> VerifyTotpEnrolmentAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent);

    // TOTP login
    Task<LoginResponse> VerifyLoginTotpAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent);

    // Email OTP login (backup factor)
    Task<MfaChallenge> SendEmailOtpAsync(Guid userId, string? ipAddress);
    Task<LoginResponse> VerifyLoginEmailOtpAsync(Guid challengeId, string otpCode, string? ipAddress, string? userAgent);

    // Admin
    Task DisableMfaAsync(Guid userId);
    Task ResetMfaAsync(Guid userId);
}
