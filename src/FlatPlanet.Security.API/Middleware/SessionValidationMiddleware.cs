using System.Security.Claims;
using System.Text.Json;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace FlatPlanet.Security.API.Middleware;

public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;

    public SessionValidationMiddleware(RequestDelegate next, IMemoryCache cache)
    {
        _next = next;
        _cache = cache;
    }

    // Paths the enrolment-only token is permitted to call.
    private static readonly HashSet<string> _enrolmentAllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/mfa/totp/begin-enrol",
        "/api/v1/mfa/totp/verify-enrol"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // Enrolment-only tokens may only reach the two enrolment endpoints.
            var enrolmentOnly = context.User.FindFirstValue("enrolment_only");
            if (enrolmentOnly == "true" && !_enrolmentAllowedPaths.Contains(context.Request.Path.Value ?? string.Empty))
            {
                context.Response.StatusCode  = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"success\":false,\"message\":\"Enrolment token may only be used to complete MFA enrollment.\"}");
                return;
            }

            var sessionIdClaim = context.User.FindFirstValue("session_id");
            if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId))
            {
                var sessions = context.RequestServices.GetRequiredService<ISessionRepository>();
                var auditLog = context.RequestServices.GetRequiredService<IAuditLogRepository>();
                var now = DateTime.UtcNow;

                var cacheKey = $"fp:sec:session:{sessionId}";

                // Try cache first
                if (!_cache.TryGetValue(cacheKey, out Session? session) || session is null)
                {
                    session = await sessions.GetByIdAsync(sessionId);
                    if (session is not null)
                    {
                        _cache.Set(cacheKey, session, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                        });
                    }
                }

                if (session == null || !session.IsActive)
                {
                    await WriteUnauthorizedAsync(context, "Session not found or already ended.");
                    return;
                }

                if (session.ExpiresAt < now)
                {
                    await sessions.EndSessionAsync(sessionId, "absolute_timeout");
                    _cache.Remove(cacheKey);

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
                    _cache.Remove(cacheKey);

                    await auditLog.LogAsync(new AuthAuditLog
                    {
                        UserId = session.UserId,
                        EventType = AuditEventType.SessionIdleTimeout,
                        Details = JsonSerializer.Serialize(new { session_id = sessionId })
                    });

                    await WriteUnauthorizedAsync(context, "Session has expired due to inactivity.");
                    return;
                }

                // Throttle last_active_at writes: only update if > 30 seconds since last update
                if ((now - session.LastActiveAt).TotalSeconds > 30)
                {
                    await sessions.UpdateLastActiveAtAsync(sessionId, now);
                    // Update cached session's LastActiveAt so idle timeout check stays accurate
                    session.LastActiveAt = now;
                    _cache.Set(cacheKey, session, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                    });
                }
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
