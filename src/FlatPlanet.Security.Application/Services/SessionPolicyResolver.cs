using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Constants;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class SessionPolicyResolver : ISessionPolicyResolver
{
    public const int DefaultAbsoluteTimeoutMinutes = 480;
    public const int DefaultIdleTimeoutMinutes     = 30;

    private readonly IAppRepository _apps;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly ILogger<SessionPolicyResolver> _logger;

    public SessionPolicyResolver(
        IAppRepository apps,
        IUserAppRoleRepository userAppRoles,
        ILogger<SessionPolicyResolver> logger)
    {
        _apps         = apps;
        _userAppRoles = userAppRoles;
        _logger       = logger;
    }

    public async Task<SessionPolicy> ResolveAsync(Guid userId, string? appSlug, IReadOnlyDictionary<string, string> config)
    {
        if (string.IsNullOrWhiteSpace(appSlug))
            return BuildPolicy(appId: null, app: null, config);

        var app = await _apps.GetBySlugAsync(appSlug);

        if (app is null)
        {
            // Not fatal — login has always accepted an arbitrary appSlug. Log it, because
            // a typo silently costs the app its session policy and that is otherwise
            // invisible until sessions start expiring earlier than expected.
            _logger.LogWarning(
                "Session policy: app slug '{AppSlug}' not found; using platform default timeouts.",
                appSlug);
            return BuildPolicy(appId: null, app: null, config);
        }

        // app_id records the app context the client claimed, so it is stamped even when the
        // override below is refused. The timeout is the privileged part, not the label.
        if (app.Status != EntityStatus.Active)
        {
            _logger.LogWarning(
                "Session policy: app '{AppSlug}' is {Status}; using platform default timeouts.",
                appSlug, app.Status);
            return BuildPolicy(app.Id, app: null, config);
        }

        // The slug is unauthenticated client input on an anonymous endpoint. Without this
        // check any account could claim any app's session lifetime by sending its slug.
        var grants = await _userAppRoles.GetActiveByUserAndAppAsync(userId, app.Id);
        if (!grants.Any())
        {
            _logger.LogWarning(
                "Session policy: user {UserId} has no active grant to app '{AppSlug}'; using platform default timeouts.",
                userId, appSlug);
            return BuildPolicy(app.Id, app: null, config);
        }

        return BuildPolicy(app.Id, app, config);
    }

    public SessionPolicy ResolveForAuthorisedApp(App? app, IReadOnlyDictionary<string, string> config) =>
        BuildPolicy(app?.Id, app, config);

    /// <summary>
    /// Builds the policy. <paramref name="app"/> is non-null only when its override has been
    /// authorised — pass null to record the app id while still using platform defaults.
    /// </summary>
    private static SessionPolicy BuildPolicy(Guid? appId, App? app, IReadOnlyDictionary<string, string> config)
    {
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var absolute = app?.SessionAbsoluteTimeoutMinutes
            ?? Cfg("session_absolute_timeout_minutes", DefaultAbsoluteTimeoutMinutes);
        var idle = app?.SessionIdleTimeoutMinutes
            ?? Cfg("session_idle_timeout_minutes", DefaultIdleTimeoutMinutes);

        // A non-positive value would expire every session for the app on creation. The DB has
        // CHECK constraints on the app columns; this also covers a bad security_config value,
        // which has no constraint anywhere.
        if (absolute <= 0) absolute = DefaultAbsoluteTimeoutMinutes;
        if (idle     <= 0) idle     = DefaultIdleTimeoutMinutes;

        return new SessionPolicy(appId, absolute, idle);
    }
}
