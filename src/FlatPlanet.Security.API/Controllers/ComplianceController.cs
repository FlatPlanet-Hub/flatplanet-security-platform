using System.Security.Claims;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class ComplianceController : ControllerBase
{
    private readonly IComplianceService _compliance;

    public ComplianceController(IComplianceService compliance) => _compliance = compliance;

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var callerId))
            throw new UnauthorizedAccessException("Invalid token: user ID claim missing.");

        var isAdmin = User.IsInRole("platform_owner") || User.IsInRole("app_admin");
        if (callerId != id && !isAdmin)
            return Forbid();

        var result = await _compliance.ExportUserDataAsync(id);
        return Ok(new { success = true, data = result });
    }

    [HttpPost("{id:guid}/anonymize")]
    [Authorize(Policy = "AdminAccess")]
    public async Task<IActionResult> Anonymize(Guid id)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var requestedBy))
            throw new UnauthorizedAccessException("Invalid token: user ID claim missing.");
        await _compliance.AnonymizeUserAsync(id, requestedBy);
        return Ok(new { success = true, message = "User data anonymized." });
    }
}
