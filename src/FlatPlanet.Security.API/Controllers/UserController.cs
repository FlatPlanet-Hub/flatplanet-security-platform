using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _users;

    public UserController(IUserService users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _users.GetAllAsync();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _users.GetByIdAsync(id);
        return Ok(new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var result = await _users.UpdateAsync(id, request);
        return Ok(new { success = true, data = result });
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateUserStatusRequest request)
    {
        await _users.UpdateStatusAsync(id, request.Status);
        return Ok(new { success = true, message = "Status updated." });
    }
}
