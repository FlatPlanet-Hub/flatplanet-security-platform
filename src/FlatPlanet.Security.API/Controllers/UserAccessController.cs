using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/users")]
[Authorize(Policy = "AdminAccess")]
public class UserAccessController : ApiController
{
    private readonly IUserAccessService _userAccess;

    public UserAccessController(IUserAccessService userAccess) => _userAccess = userAccess;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _userAccess.GetByAppIdAsync(appId);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> GrantAccess(Guid appId, [FromBody] GrantUserAccessRequest request)
    {
        var grantedBy = GetUserId();
        var result = await _userAccess.GrantAccessAsync(appId, request, grantedBy);
        return Created201(result);
    }

    [HttpPut("{userId:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid appId, Guid userId, [FromBody] UpdateUserRoleRequest request)
    {
        await _userAccess.UpdateRoleAsync(appId, userId, request.RoleId);
        return OkMessage("Role updated.");
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RevokeAccess(Guid appId, Guid userId)
    {
        await _userAccess.RevokeAccessAsync(appId, userId);
        return OkMessage("Access revoked.");
    }
}
