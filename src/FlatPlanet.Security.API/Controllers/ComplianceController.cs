using FlatPlanet.Security.API.Authorization;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class ComplianceController : ApiController
{
    private readonly IComplianceService _compliance;

    public ComplianceController(IComplianceService compliance) => _compliance = compliance;

    // Compliance export: any user can pull their own data; admins / audit:read
    // service tokens can pull anyone's.
    [HttpGet("{id:guid}/export")]
    [RequireScope("audit:read")]
    public async Task<IActionResult> Export(Guid id)
    {
        var callerId = GetUserId();

        var isAdmin = User.IsInRole("platform_owner") || User.IsInRole("app_admin");
        if (callerId != id && !isAdmin)
            return Forbid();

        var result = await _compliance.ExportUserDataAsync(id);
        return OkData(result);
    }

    [HttpPost("{id:guid}/anonymize")]
    [Authorize(Policy = "AdminAccess")]
    [RequireScope("users:write")]
    public async Task<IActionResult> Anonymize(Guid id)
    {
        var requestedBy = GetUserId();
        await _compliance.AnonymizeUserAsync(id, requestedBy);
        return OkMessage("User data anonymized.");
    }
}
