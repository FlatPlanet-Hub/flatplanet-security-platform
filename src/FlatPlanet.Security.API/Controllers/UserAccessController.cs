using System.Security.Claims;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/users")]
[Authorize(Policy = "AdminAccess")]
public class UserAccessController : ControllerBase
{
    private readonly IUserAccessService _userAccess;

    public UserAccessController(IUserAccessService userAccess) => _userAccess = userAccess;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _userAccess.GetByAppIdAsync(appId);
        return Ok(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> GrantAccess(Guid appId, [FromBody] GrantUserAccessRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var grantedBy))
            throw new UnauthorizedAccessException("Invalid token: user ID claim missing.");
        var result = await _userAccess.GrantAccessAsync(appId, request, grantedBy);
        return StatusCode(201, new { success = true, data = result });
    }

    [HttpPut("{userId:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid appId, Guid userId, [FromBody] UpdateUserRoleRequest request)
    {
        await _userAccess.UpdateRoleAsync(appId, userId, request.RoleId);
        return Ok(new { success = true, message = "Role updated." });
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RevokeAccess(Guid appId, Guid userId)
    {
        await _userAccess.RevokeAccessAsync(appId, userId);
        return Ok(new { success = true, message = "Access revoked." });
    }
}
