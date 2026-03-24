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
    public async Task<IActionResult> Authorize([FromBody] AuthorizeRequest request)
    {
        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.AppSlug))
            return BadRequest(new { success = false, message = "userId and appSlug are required." });

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authorizationService.AuthorizeAsync(request, ipAddress);
        return Ok(new { success = true, data = result });
    }
}
