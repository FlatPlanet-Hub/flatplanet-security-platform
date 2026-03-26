using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "AdminAccess")]
public class OffboardingController : ApiController
{
    private readonly IOffboardingService _offboarding;

    public OffboardingController(IOffboardingService offboarding) => _offboarding = offboarding;

    [HttpPost("{id:guid}/offboard")]
    public async Task<IActionResult> Offboard(Guid id)
    {
        var requestedBy = GetUserId();
        await _offboarding.OffboardAsync(id, requestedBy);
        return OkMessage("User offboarded successfully.");
    }
}
