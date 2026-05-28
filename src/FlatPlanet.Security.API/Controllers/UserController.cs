using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/users")]
// Both schemes accepted: ServiceToken (HubApi calling SP) and JwtBearer (user logged in via hub).
// Mutating endpoints additionally require the AdminAccess policy below.
[Authorize(AuthenticationSchemes = "ServiceToken," + JwtBearerDefaults.AuthenticationScheme)]
public class UserController : ApiController
{
    private readonly IUserService _users;

    public UserController(IUserService users) => _users = users;

    // Admin only — creating users is a sensitive operation
    [HttpPost]
    [Authorize(Policy = "AdminAccess")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var result = await _users.CreateAsync(request);
        return Created201(result);
    }

    // Any authenticated user — needed by project owners (e.g. via hub member-invite UI)
    // to look up existing users by name/email before inviting them to a project.
    // Also called by HubApi via ServiceToken for member-management flows.
    // Response contains no secrets (no password hash, no tokens) — just profile fields.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] UserQueryParams query)
    {
        var result = await _users.GetPagedAsync(query);
        return OkData(result);
    }

    // Any authenticated user — needed when the hub renders a member's display info,
    // and by HubApi (via ServiceToken) when looking up user profile for project members.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _users.GetByIdAsync(id);
        return OkData(result);
    }

    // Admin only — updating someone else's profile is a sensitive operation.
    // (Users update their OWN profile via PATCH /api/v1/auth/me, not this endpoint.)
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminAccess")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var result = await _users.UpdateAsync(id, request);
        return OkData(result);
    }

    // Admin only — activating/deactivating users is a sensitive operation.
    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "AdminAccess")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateUserStatusRequest request)
    {
        await _users.UpdateStatusAsync(id, request.Status);
        return OkMessage("Status updated.");
    }
}
