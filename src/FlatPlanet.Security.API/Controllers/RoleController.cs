using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/roles")]
[Authorize(Policy = "AdminAccess")]
public class RoleController : ApiController
{
    private readonly IRoleService _roles;

    public RoleController(IRoleService roles) => _roles = roles;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _roles.GetByAppIdAsync(appId);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreateRoleRequest request)
    {
        var result = await _roles.CreateAsync(appId, request);
        return Created201(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid appId, Guid id, [FromBody] UpdateRoleRequest request)
    {
        var result = await _roles.UpdateAsync(appId, id, request);
        return OkData(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid appId, Guid id)
    {
        await _roles.DeleteAsync(appId, id);
        return OkMessage("Role deleted.");
    }

    [HttpPost("{roleId:guid}/permissions")]
    public async Task<IActionResult> AssignPermission(Guid appId, Guid roleId, [FromBody] AssignPermissionRequest request)
    {
        var userId = GetUserId();
        await _roles.AssignPermissionAsync(roleId, request.PermissionId, userId);
        return OkMessage("Permission assigned.");
    }

    [HttpDelete("{roleId:guid}/permissions/{permId:guid}")]
    public async Task<IActionResult> RemovePermission(Guid appId, Guid roleId, Guid permId)
    {
        await _roles.RemovePermissionAsync(roleId, permId);
        return OkMessage("Permission removed.");
    }
}
