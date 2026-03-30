using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/mfa")]
public class MfaController : ApiController
{
    private readonly IMfaService _mfa;

    public MfaController(IMfaService mfa) => _mfa = mfa;

    [HttpPost("enroll")]
    [Authorize]
    public async Task<IActionResult> Enroll([FromBody] EnrollPhoneRequest request)
    {
        var userId = GetUserId();
        var result = await _mfa.EnrollAndSendOtpAsync(userId, request.PhoneNumber);
        return OkData(result);
    }

    [HttpPost("otp/verify")]
    [Authorize]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var userId = GetUserId();
        await _mfa.VerifyOtpAsync(userId, request.Code);
        return OkData(new { message = "MFA enrollment verified. MFA is now enabled on your account." });
    }

    [HttpPost("otp/login-verify")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginVerify([FromBody] MfaLoginVerifyRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await _mfa.VerifyLoginOtpAsync(request.ChallengeId, request.Code, ipAddress, userAgent);
        return OkData(result);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
