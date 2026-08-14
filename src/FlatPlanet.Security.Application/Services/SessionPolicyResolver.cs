using System.Text.Json;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Constants;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class SessionPolicyResolver : ISessionPolicyResolver
{
    public const int DefaultAbsoluteTimeoutMinutes = 480;
    public const int DefaultIdleTimeoutMinutes     = 30;

    private readonly IAppRepository _apps;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<SessionPolicyResolver> _logger;

    public SessionPolicyResolver(
        IAppRepository apps,
        IUserAppRoleRepository userAppRoles,
        IAuditLogRepository auditLog,
        ILogger<SessionPolicyResolver> logger)
    {
        _apps         = apps;
        _userAppRoles = userAppRoles;
        _auditLog     = auditLog;
        _logger       = logger;
    }

    public async Task<SessionPolicy> ResolveAsync(Guid userId, string? appSlug, IReadOnlyDictionary<string, string> config)
    {
        if (string.IsNullOrWhiteSpace(appSlug))
            return BuildPolicy(null, config);

        var app = await _apps.GetBySlugAsync(appSlug);

        if (app is null)
            return await DenyAsync(userId, appSlug, "app_not_found", config);

        if (app.Status != EntityStatus.Active)
            return await DenyAsync(userId, appSlug, "app_inactive", config);

        // The slug is unauthenticated client input on an anonymous endpoint. Without this
        // check any account could claim any app's session lifetime by sending its slug.
        var grants = await _userAppRoles.GetActiveByUserAndAppAsync(userId, app.Id);
        if (!grants.Any())
            return await DenyAsync(userId, appSlug, "no_app_grant", config);

        return BuildPolicy(app, config);
    }

    public SessionPolicy ResolveForAuthorisedApp(App? app, IReadOnlyDictionary<string, string> config) =>
        BuildPolicy(app, config);

    /// <summary>
    /// Refuses the override and records why. The login still succeeds on platform defaults,
    /// so this is not a login failure — but it is the only durable trace that a slug was
    /// claimed and rejected, and the application log is not an audit surface.
    /// </summary>
    private async Task<SessionPolicy> DenyAsync(
        Guid userId, string appSlug, string reason, IReadOnlyDictionary<string, string> config)
    {
        _logger.LogWarning(
            "Session policy: refused app '{AppSlug}' for user {UserId} ({Reason}); using platform default timeouts.",
            appSlug, userId, reason);

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.SessionPolicyDenied,
            Details   = JsonSerializer.Serialize(new { app_slug = appSlug, reason })
        });

        // The app association is unverified, so it is NOT recorded on the session.
        // sessions.app_id must mean "this session belongs to that app" and nothing else.
        return BuildPolicy(null, config);
    }

    /// <summary>
    /// Builds the policy from an app whose association with the user has been verified.
    /// Pass null when there is no verified app — the session then gets platform defaults
    /// and no app id.
    /// </summary>
    private static SessionPolicy BuildPolicy(App? verifiedApp, IReadOnlyDictionary<string, string> config)
    {
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var absolute = Cfg("session_absolute_timeout_minutes", DefaultAbsoluteTimeoutMinutes);
        var idle     = Cfg("session_idle_timeout_minutes", DefaultIdleTimeoutMinutes);

        // Overrides apply only to ACTIVE apps. Suspending an app is the operator's kill
        // switch for its long-lived sessions, so the check lives here rather than in the
        // callers — every login path goes through this method, including the federated one
        // that authorises its own app and calls ResolveForAuthorisedApp.
        if (verifiedApp is { Status: EntityStatus.Active })
        {
            absolute = verifiedApp.SessionAbsoluteTimeoutMinutes ?? absolute;
            idle     = verifiedApp.SessionIdleTimeoutMinutes     ?? idle;
        }

        // A non-positive value would expire every session for the app on creation. The DB has
        // CHECK constraints on the app columns; this also covers a bad security_config value,
        // which has no constraint anywhere.
        if (absolute <= 0) absolute = DefaultAbsoluteTimeoutMinutes;
        if (idle     <= 0) idle     = DefaultIdleTimeoutMinutes;

        return new SessionPolicy(verifiedApp?.Id, absolute, idle);
    }
}
