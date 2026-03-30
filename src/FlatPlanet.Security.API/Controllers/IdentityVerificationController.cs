using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/identity/verification")]
public class IdentityVerificationController : ApiController
{
    private readonly IIdentityVerificationService _service;

    public IdentityVerificationController(IIdentityVerificationService service) => _service = service;

    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> GetStatus()
    {
        var result = await _service.GetStatusAsync(GetUserId());
        return OkData(result);
    }

    [HttpGet("service/status/{userId:guid}")]
    [Authorize(Policy = "PlatformOwner")]
    public async Task<IActionResult> GetStatusForService(Guid userId)
    {
        var result = await _service.GetStatusAsync(userId);
        return OkData(result);
    }
}
