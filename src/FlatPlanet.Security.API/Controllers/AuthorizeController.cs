using System.Security.Claims;
using FlatPlanet.Security.Application.DTOs.Authorization;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class AuthorizeController : ControllerBase
{
    private readonly IAccessAuthorizationService _authorizationService;

    public AuthorizeController(IAccessAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize([FromBody] AuthorizeRequestBody body)
    {
        if (string.IsNullOrWhiteSpace(body.AppSlug))
            return BadRequest(new { success = false, message = "appSlug is required." });

        // Fix 2: userId always comes from the JWT — never from the request body
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized(new { success = false, message = "Invalid token." });

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var request = new AuthorizeRequest
        {
            UserId = userId,
            AppSlug = body.AppSlug,
            ResourceIdentifier = body.ResourceIdentifier,
            RequiredPermission = body.RequiredPermission
        };

        var result = await _authorizationService.AuthorizeAsync(request, ipAddress);
        return Ok(new { success = true, data = result });
    }
}
