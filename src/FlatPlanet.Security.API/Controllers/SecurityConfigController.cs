using System.Security.Claims;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/security/config")]
[Authorize]
public class SecurityConfigController : ControllerBase
{
    private readonly ISecurityConfigService _config;

    public SecurityConfigController(ISecurityConfigService config) => _config = config;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _config.GetAllAsync();
        return Ok(new { success = true, data = result });
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateConfigRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid.TryParse(sub, out var userId);
        await _config.UpdateAsync(key, request.Value, userId);
        return Ok(new { success = true, message = "Config updated." });
    }
}

public class UpdateConfigRequest
{
    public string Value { get; set; } = string.Empty;
}
