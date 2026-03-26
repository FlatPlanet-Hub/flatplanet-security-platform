using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = "AdminAccess")]
public class UserController : ApiController
{
    private readonly IUserService _users;

    public UserController(IUserService users) => _users = users;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var result = await _users.CreateAsync(request);
        return Created201(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] UserQueryParams query)
    {
        var result = await _users.GetPagedAsync(query);
        return OkData(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _users.GetByIdAsync(id);
        return OkData(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var result = await _users.UpdateAsync(id, request);
        return OkData(result);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateUserStatusRequest request)
    {
        await _users.UpdateStatusAsync(id, request.Status);
        return OkMessage("Status updated.");
    }
}
