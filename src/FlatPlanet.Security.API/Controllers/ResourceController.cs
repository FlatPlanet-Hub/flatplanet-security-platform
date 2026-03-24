using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/resources")]
[Authorize]
public class ResourceController : ControllerBase
{
    private readonly IResourceService _resources;

    public ResourceController(IResourceService resources) => _resources = resources;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _resources.GetByAppIdAsync(appId);
        return Ok(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreateResourceRequest request)
    {
        var result = await _resources.CreateAsync(appId, request);
        return StatusCode(201, new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid appId, Guid id, [FromBody] UpdateResourceRequest request)
    {
        var result = await _resources.UpdateAsync(appId, id, request);
        return Ok(new { success = true, data = result });
    }
}
