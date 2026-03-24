using System.Security.Claims;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps")]
[Authorize(Policy = "AdminAccess")]
public class AppController : ControllerBase
{
    private readonly IAppService _apps;

    public AppController(IAppService apps) => _apps = apps;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _apps.GetAllAsync();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _apps.GetByIdAsync(id);
        return Ok(new { success = true, data = result });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Invalid token: user ID claim missing.");
        var result = await _apps.CreateAsync(request, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAppRequest request)
    {
        var result = await _apps.UpdateAsync(id, request);
        return Ok(new { success = true, data = result });
    }
}
