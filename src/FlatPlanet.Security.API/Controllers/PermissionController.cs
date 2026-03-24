using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/permissions")]
[Authorize(Policy = "AdminAccess")]
public class PermissionController : ControllerBase
{
    private readonly IPermissionService _permissions;

    public PermissionController(IPermissionService permissions) => _permissions = permissions;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _permissions.GetByAppIdAsync(appId);
        return Ok(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreatePermissionRequest request)
    {
        var result = await _permissions.CreateAsync(appId, request);
        return StatusCode(201, new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid appId, Guid id, [FromBody] UpdatePermissionRequest request)
    {
        var result = await _permissions.UpdateAsync(appId, id, request);
        return Ok(new { success = true, data = result });
    }
}
