using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps")]
[Authorize(Policy = "AdminAccess")]
public class AppController : ApiController
{
    private readonly IAppService _apps;

    public AppController(IAppService apps) => _apps = apps;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _apps.GetAllAsync();
        return OkData(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _apps.GetByIdAsync(id);
        return OkData(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppRequest request)
    {
        var userId = GetUserId();
        var result = await _apps.CreateAsync(request, userId);
        return CreatedData(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAppRequest request)
    {
        var result = await _apps.UpdateAsync(id, request);
        return OkData(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _apps.DeleteAsync(id);
        return OkMessage("App permanently deleted.");
    }
}
