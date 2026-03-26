using FlatPlanet.Security.Application.DTOs.Authorization;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class AuthorizeController : ApiController
{
    private readonly IAccessAuthorizationService _authorizationService;

    public AuthorizeController(IAccessAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize([FromBody] AuthorizeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AppSlug))
            return FailBadRequest("appSlug is required.");

        if (!TryGetUserId(out var userId))
            return FailUnauthorized();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _authorizationService.AuthorizeAsync(userId, request, ipAddress);
        return OkData(result);
    }
}
