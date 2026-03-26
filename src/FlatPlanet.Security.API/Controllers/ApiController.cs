using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace FlatPlanet.Security.API.Controllers;

public abstract class ApiController : ControllerBase
{
    protected IActionResult OkData(object? data) =>
        Ok(new { success = true, data });

    protected IActionResult Created201(object? data) =>
        StatusCode(201, new { success = true, data });

    protected IActionResult CreatedData(string actionName, object routeValues, object? data) =>
        CreatedAtAction(actionName, routeValues, new { success = true, data });

    protected IActionResult OkMessage(string message) =>
        Ok(new { success = true, message });

    protected IActionResult FailBadRequest(string message) =>
        BadRequest(new { success = false, message });

    protected IActionResult FailUnauthorized(string message = "Invalid token.") =>
        Unauthorized(new { success = false, message });

    protected Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Invalid token: user ID claim missing.");
        return Guid.Parse(sub);
    }

    protected bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out userId);
    }

    protected Guid? TryGetSessionId()
    {
        var sessionId = User.FindFirstValue("session_id");
        return Guid.TryParse(sessionId, out var id) ? id : null;
    }
}
