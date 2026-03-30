using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace FlatPlanet.Security.Application.Helpers;

public static class ActorContext
{
    public static Guid GetActorId(IHttpContextAccessor httpContext)
    {
        var sub = httpContext.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? httpContext.HttpContext?.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    public static string GetActorEmail(IHttpContextAccessor httpContext) =>
        httpContext.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
        ?? httpContext.HttpContext?.User.FindFirst("email")?.Value
        ?? "unknown";

    public static string? GetIpAddress(IHttpContextAccessor httpContext) =>
        httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
