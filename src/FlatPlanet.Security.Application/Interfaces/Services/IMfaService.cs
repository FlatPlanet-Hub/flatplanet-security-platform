using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IMfaService
{
    Task<EnrollPhoneResponse> EnrollAndSendOtpAsync(Guid userId, string phoneNumber);
    Task VerifyOtpAsync(Guid userId, string code);
    Task<MfaChallenge> SendLoginOtpAsync(Guid userId, string phoneNumber);
    Task<LoginResponse> VerifyLoginOtpAsync(Guid challengeId, string code, string? ipAddress, string? userAgent);
}
