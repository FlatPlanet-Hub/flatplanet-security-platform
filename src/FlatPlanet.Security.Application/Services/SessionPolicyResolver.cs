using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class SessionPolicyResolver : ISessionPolicyResolver
{
    public const int DefaultAbsoluteTimeoutMinutes = 480;
    public const int DefaultIdleTimeoutMinutes     = 30;

    private readonly IAppRepository _apps;
    private readonly ILogger<SessionPolicyResolver> _logger;

    public SessionPolicyResolver(IAppRepository apps, ILogger<SessionPolicyResolver> logger)
    {
        _apps   = apps;
        _logger = logger;
    }

    public async Task<SessionPolicy> ResolveAsync(string? appSlug, IReadOnlyDictionary<string, string> config)
    {
        if (string.IsNullOrWhiteSpace(appSlug))
            return Resolve(null, config);

        var app = await _apps.GetBySlugAsync(appSlug);

        if (app is null)
        {
            // Not fatal: login has always accepted an arbitrary appSlug. Fall back to
            // platform defaults, but log it — a typo here silently costs the app its
            // session policy, which is otherwise invisible until sessions expire early.
            _logger.LogWarning(
                "Session policy: app slug '{AppSlug}' not found; falling back to platform default timeouts.",
                appSlug);
        }

        return Resolve(app, config);
    }

    public SessionPolicy Resolve(App? app, IReadOnlyDictionary<string, string> config)
    {
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var absolute = app?.SessionAbsoluteTimeoutMinutes
            ?? Cfg("session_absolute_timeout_minutes", DefaultAbsoluteTimeoutMinutes);
        var idle = app?.SessionIdleTimeoutMinutes
            ?? Cfg("session_idle_timeout_minutes", DefaultIdleTimeoutMinutes);

        // A non-positive override would expire every session for the app on creation.
        // The DB has CHECK constraints for this; guard anyway so a bad platform-config
        // value cannot lock an app out either.
        if (absolute <= 0) absolute = DefaultAbsoluteTimeoutMinutes;
        if (idle     <= 0) idle     = DefaultIdleTimeoutMinutes;

        return new SessionPolicy(app?.Id, absolute, idle);
    }
}
