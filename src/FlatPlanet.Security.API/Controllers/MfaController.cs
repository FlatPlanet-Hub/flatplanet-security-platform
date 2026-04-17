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

    // ── TOTP Enrolment ───────────────────────────────────────────────────────

    [HttpPost("totp/begin-enrol")]
    [Authorize]
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

    // ── Email OTP Login ──────────────────────────────────────────────────────

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
}
