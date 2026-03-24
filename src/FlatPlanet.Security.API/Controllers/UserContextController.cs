using System.Security.Claims;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps")]
[Authorize]
public class UserContextController : ControllerBase
{
    private readonly IUserContextService _userContextService;

    public UserContextController(IUserContextService userContextService)
    {
        _userContextService = userContextService;
    }

    [HttpGet("{appSlug}/user-context")]
    public async Task<IActionResult> GetUserContext(string appSlug)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized(new { success = false, message = "Invalid token." });

        var result = await _userContextService.GetUserContextAsync(userId, appSlug);
        return Ok(new { success = true, data = result });
    }
}
