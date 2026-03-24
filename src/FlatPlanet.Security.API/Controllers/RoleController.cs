using System.Security.Claims;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/roles")]
[Authorize(Policy = "AdminAccess")]
public class RoleController : ControllerBase
{
    private readonly IRoleService _roles;

    public RoleController(IRoleService roles) => _roles = roles;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _roles.GetByAppIdAsync(appId);
        return Ok(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreateRoleRequest request)
    {
        var result = await _roles.CreateAsync(appId, request);
        return StatusCode(201, new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid appId, Guid id, [FromBody] UpdateRoleRequest request)
    {
        var result = await _roles.UpdateAsync(appId, id, request);
        return Ok(new { success = true, data = result });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid appId, Guid id)
    {
        await _roles.DeleteAsync(appId, id);
        return Ok(new { success = true, message = "Role deleted." });
    }

    [HttpPost("{roleId:guid}/permissions")]
    public async Task<IActionResult> AssignPermission(Guid appId, Guid roleId, [FromBody] AssignPermissionRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Invalid token: user ID claim missing.");
        await _roles.AssignPermissionAsync(roleId, request.PermissionId, userId);
        return Ok(new { success = true, message = "Permission assigned." });
    }

    [HttpDelete("{roleId:guid}/permissions/{permId:guid}")]
    public async Task<IActionResult> RemovePermission(Guid appId, Guid roleId, Guid permId)
    {
        await _roles.RemovePermissionAsync(roleId, permId);
        return Ok(new { success = true, message = "Permission removed." });
    }
}
