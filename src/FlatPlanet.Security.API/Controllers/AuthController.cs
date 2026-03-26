using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
}
