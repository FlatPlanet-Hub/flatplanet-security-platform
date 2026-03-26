using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/permissions")]
[Authorize(Policy = "AdminAccess")]
public class PermissionController : ApiController
{
    private readonly IPermissionService _permissions;

    public PermissionController(IPermissionService permissions) => _permissions = permissions;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _permissions.GetByAppIdAsync(appId);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreatePermissionRequest request)
    {
        var result = await _permissions.CreateAsync(appId, request);
        return Created201(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid appId, Guid id, [FromBody] UpdatePermissionRequest request)
    {
        var result = await _permissions.UpdateAsync(appId, id, request);
        return OkData(result);
    }
}
