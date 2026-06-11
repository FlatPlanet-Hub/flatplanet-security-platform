using FlatPlanet.Security.API.Authorization;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/resource-types")]
[Authorize(Policy = "AdminAccess")]
public class ResourceTypeController : ApiController
{
    private readonly IResourceTypeService _resourceTypes;

    public ResourceTypeController(IResourceTypeService resourceTypes) => _resourceTypes = resourceTypes;

    [HttpGet]
    [RequireScope("resources:read")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _resourceTypes.GetAllAsync();
        return OkData(result);
    }

    [HttpPost]
    [RequireScope("resources:write")]
    public async Task<IActionResult> Create([FromBody] CreateResourceTypeRequest request)
    {
        var result = await _resourceTypes.CreateAsync(request);
        return Created201(result);
    }
}
