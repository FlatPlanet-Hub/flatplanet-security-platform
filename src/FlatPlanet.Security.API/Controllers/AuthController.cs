using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return FailBadRequest("Email and password are required.");

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginAsync(request, ipAddress, userAgent);
        return OkData(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = GetUserId();
        var sessionId = TryGetSessionId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await _authService.LogoutAsync(sessionId, userId, ipAddress);
        return OkMessage("Logged out successfully.");
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return FailBadRequest("Refresh token is required.");

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RefreshAsync(request, ipAddress);
        return OkData(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me([FromQuery] string? appSlug)
    {
        var userId = GetUserId();
        var profile = await _authService.GetProfileAsync(userId, appSlug);
        return OkData(profile);
    }

    [Authorize]
    [EnableRateLimiting("change-password")]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _authService.ChangePasswordAsync(userId, request, ipAddress);
        return OkMessage("Password changed. Please log in again.");
    }

    [Authorize]
    [EnableRateLimiting("update-profile")]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.UpdateProfileAsync(userId, request, ipAddress);
        return OkData(result);
    }

    [EnableRateLimiting("forgot-password")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _authService.ForgotPasswordAsync(request);
        return OkMessage("If that email exists, a reset link has been sent.");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _authService.ResetPasswordAsync(request, ipAddress);
        return OkMessage("Password reset successfully. Please log in.");
    }

    /// <summary>
    /// Keeps the session alive by resetting the idle timeout.
    /// Call every 10-15 minutes from long-lived clients (e.g. dashboards).
    /// SessionValidationMiddleware handles the last_active_at update automatically.
    /// </summary>
    [Authorize]
    [HttpPost("heartbeat")]
    public IActionResult Heartbeat()
    {
        return OkData(new { sessionActive = true });
    }
}
