using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

[ApiController]
[Route("api/v1/apps")]
[Authorize]
public class UserContextController : ApiController
{
    private readonly IUserContextService _userContextService;

    public UserContextController(IUserContextService userContextService)
    {
        _userContextService = userContextService;
    }

    [HttpGet("{appSlug}/user-context")]
    public async Task<IActionResult> GetUserContext(string appSlug)
    {
        if (!TryGetUserId(out var userId))
            return FailUnauthorized();

        var result = await _userContextService.GetUserContextAsync(userId, appSlug);
        return OkData(result);
    }
}
