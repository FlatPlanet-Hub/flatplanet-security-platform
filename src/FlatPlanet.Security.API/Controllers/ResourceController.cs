using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps/{appId:guid}/resources")]
[Authorize(Policy = "AdminAccess")]
public class ResourceController : ApiController
{
    private readonly IResourceService _resources;

    public ResourceController(IResourceService resources) => _resources = resources;

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid appId)
    {
        var result = await _resources.GetByAppIdAsync(appId);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid appId, [FromBody] CreateResourceRequest request)
    {
        var result = await _resources.CreateAsync(appId, request);
        return Created201(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid appId, Guid id, [FromBody] UpdateResourceRequest request)
    {
        var result = await _resources.UpdateAsync(appId, id, request);
        return OkData(result);
    }
}
