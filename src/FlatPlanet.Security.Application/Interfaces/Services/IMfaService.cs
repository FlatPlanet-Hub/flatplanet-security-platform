using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IMfaService
{
    // TOTP enrolment
    Task<BeginTotpEnrolmentResponse> BeginTotpEnrolmentAsync(Guid userId);
    Task<LoginResponse> VerifyTotpEnrolmentAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent, string? appSlug = null);

    // TOTP login
    Task<LoginResponse> VerifyLoginTotpAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent, string? appSlug = null);

    // Email OTP login (backup factor)
    Task<MfaChallenge> SendEmailOtpAsync(Guid userId, string? ipAddress);
    Task<MfaChallenge> ResendEmailOtpAsync(Guid userId, string? ipAddress);
    Task<MfaChallenge> RequestTotpFallbackAsync(Guid userId, string? ipAddress);
    Task<LoginResponse> VerifyLoginEmailOtpAsync(Guid challengeId, string otpCode, string? ipAddress, string? userAgent, string? appSlug = null);

    // Backup codes (TOTP recovery)
    Task<GenerateBackupCodesResponse> GenerateBackupCodesAsync(Guid userId);
    Task<LoginResponse> VerifyBackupCodeAsync(Guid userId, string backupCode, string? ipAddress, string? userAgent, string? appSlug = null);

    // Status
    Task<UserMfaStatusResponse> GetMfaStatusAsync(Guid userId);

    // Admin
    Task DisableMfaAsync(Guid userId);
    Task ResetMfaAsync(Guid userId);
    Task SetMfaMethodAsync(Guid userId, string method, Guid performedByUserId);
}
