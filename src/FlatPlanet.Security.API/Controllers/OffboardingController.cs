using System.Security.Claims;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class OffboardingController : ControllerBase
{
    private readonly IOffboardingService _offboarding;

    public OffboardingController(IOffboardingService offboarding) => _offboarding = offboarding;

    [HttpPost("{id:guid}/offboard")]
    public async Task<IActionResult> Offboard(Guid id)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid.TryParse(sub, out var requestedBy);
        await _offboarding.OffboardAsync(id, requestedBy);
        return Ok(new { success = true, message = "User offboarded successfully." });
    }
}
