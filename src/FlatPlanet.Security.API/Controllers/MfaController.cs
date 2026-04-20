using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/mfa")]
public class MfaController : ApiController
{
    private readonly IMfaService _mfa;

    public MfaController(IMfaService mfa) => _mfa = mfa;

    // ── Status ───────────────────────────────────────────────────────────────

    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetStatus()
    {
        var result = await _mfa.GetMfaStatusAsync(GetUserId());
        return OkData(result);
    }

    // ── TOTP Enrolment ───────────────────────────────────────────────────────

    [HttpPost("totp/begin-enrol")]
    [Authorize]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> BeginTotpEnrolment()
    {
        var result = await _mfa.BeginTotpEnrolmentAsync(GetUserId());
        return OkData(result);
    }

    [HttpPost("totp/verify-enrol")]
    [Authorize]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> VerifyTotpEnrolment([FromBody] VerifyTotpEnrolmentRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await _mfa.VerifyTotpEnrolmentAsync(GetUserId(), request.TotpCode, ipAddress, userAgent);
        return OkData(result);
    }

    // ── TOTP Login ───────────────────────────────────────────────────────────

    [HttpPost("totp/login-verify")]
    [AllowAnonymous]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> VerifyLoginTotp([FromBody] VerifyLoginTotpRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await _mfa.VerifyLoginTotpAsync(request.UserId, request.TotpCode, ipAddress, userAgent);
        return OkData(result);
    }

    [HttpPost("totp/request-email-fallback")]
    [AllowAnonymous]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> RequestTotpEmailFallback([FromBody] RequestTotpFallbackRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        try
        {
            var result = await _mfa.RequestTotpFallbackAsync(request.UserId, ipAddress);
            return OkData(new { challengeId = result.Id });
        }
        catch (KeyNotFoundException)
        {
            // Safe generic response — never reveal whether userId exists or has totp enabled
            return OkData(new { challengeId = Guid.NewGuid() });
        }
    }

    // ── Email OTP ────────────────────────────────────────────────────────────

    [HttpPost("email-otp/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> ResendEmailOtp([FromBody] ResendEmailOtpRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        try
        {
            var result = await _mfa.ResendEmailOtpAsync(request.UserId, ipAddress);
            return OkData(new { challengeId = result.Id });
        }
        catch (KeyNotFoundException)
        {
            // Safe generic response — never reveal whether userId exists or has email_otp enabled
            return OkData(new { challengeId = Guid.NewGuid() });
        }
    }

    [HttpPost("email-otp/login-verify")]
    [AllowAnonymous]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> VerifyLoginEmailOtp([FromBody] MfaLoginVerifyRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await _mfa.VerifyLoginEmailOtpAsync(request.ChallengeId, request.OtpCode, ipAddress, userAgent);
        return OkData(result);
    }

    // ── Backup Codes ─────────────────────────────────────────────────────────

    [HttpPost("backup-codes/generate")]
    [Authorize]
    public async Task<IActionResult> GenerateBackupCodes()
    {
        var result = await _mfa.GenerateBackupCodesAsync(GetUserId());
        return OkData(result);
    }

    [HttpPost("backup-code/login-verify")]
    [AllowAnonymous]
    [EnableRateLimiting("mfa-verify")]
    public async Task<IActionResult> VerifyBackupCode([FromBody] VerifyBackupCodeRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await _mfa.VerifyBackupCodeAsync(request.UserId, request.BackupCode, ipAddress, userAgent);
        return OkData(result);
    }
}
