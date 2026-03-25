using System.Security.Claims;
using System.Text.Json;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;

namespace FlatPlanet.Security.API.Middleware;

public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;

    public SessionValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionIdClaim = context.User.FindFirstValue("session_id");
            if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
            {
                var sessions = context.RequestServices.GetRequiredService<ISessionRepository>();
                var auditLog = context.RequestServices.GetRequiredService<IAuditLogRepository>();
                var now = DateTime.UtcNow;

                var session = await sessions.GetByIdAsync(sessionId);

                if (session == null || !session.IsActive)
                {
                    await WriteUnauthorizedAsync(context, "Session not found or already ended.");
                    return;
                }

                if (session.ExpiresAt < now)
                {
                    await sessions.EndSessionAsync(sessionId, "absolute_timeout");
                    await auditLog.LogAsync(new AuthAuditLog
                    {
                        UserId = session.UserId,
                        EventType = AuditEventType.SessionAbsoluteTimeout,
                        Details = JsonSerializer.Serialize(new { session_id = sessionId })
                    });
                    await WriteUnauthorizedAsync(context, "Session has expired.");
                    return;
                }

                if (session.LastActiveAt.AddMinutes(session.IdleTimeoutMinutes) < now)
                {
                    await sessions.EndSessionAsync(sessionId, "idle_timeout");
                    await auditLog.LogAsync(new AuthAuditLog
                    {
                        UserId = session.UserId,
                        EventType = AuditEventType.SessionIdleTimeout,
                        Details = JsonSerializer.Serialize(new { session_id = sessionId })
                    });
                    await WriteUnauthorizedAsync(context, "Session has expired due to inactivity.");
                    return;
                }

                await sessions.UpdateLastActiveAtAsync(sessionId, now);
            }
        }

        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { success = false, message }));
    }
}
