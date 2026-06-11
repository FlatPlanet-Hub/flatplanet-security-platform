using FlatPlanet.Security.API.Authorization;
using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/admin/mfa")]
[Authorize(Policy = "AdminAccess")]
public class AdminMfaController : ApiController
{
    private readonly IMfaService _mfa;

    public AdminMfaController(IMfaService mfa) => _mfa = mfa;

    [HttpPost("{userId}/disable")]
    [RequireScope("users:write")]
    public async Task<IActionResult> DisableMfa(Guid userId)
    {
        await _mfa.DisableMfaAsync(userId);
        return OkData(new { message = "MFA disabled for user." });
    }

    [HttpPost("{userId}/reset")]
    [RequireScope("users:write")]
    public async Task<IActionResult> ResetMfa(Guid userId)
    {
        await _mfa.ResetMfaAsync(userId);
        return OkData(new { message = "MFA reset for user. User must re-enrol." });
    }

    [HttpPost("{userId}/set-method")]
    [RequireScope("users:write")]
    public async Task<IActionResult> SetMfaMethod(Guid userId, [FromBody] SetMfaMethodRequest request)
    {
        await _mfa.SetMfaMethodAsync(userId, request.Method, GetUserId());
        return OkData(new { message = $"MFA method set to '{request.Method}'. User must complete enrolment on next login." });
    }
}
