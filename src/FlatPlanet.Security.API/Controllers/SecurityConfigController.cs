using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/security/config")]
[Authorize(Policy = "PlatformOwner")]
public class SecurityConfigController : ApiController
{
    private readonly ISecurityConfigService _config;

    public SecurityConfigController(ISecurityConfigService config) => _config = config;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var result = await _config.GetAllAsync();
        return OkData(result);
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateConfigRequest request)
    {
        var userId = GetUserId();
        await _config.UpdateAsync(key, request.Value, userId);
        return OkMessage("Config updated.");
    }
}

public class UpdateConfigRequest
{
    public string Value { get; set; } = string.Empty;
}
