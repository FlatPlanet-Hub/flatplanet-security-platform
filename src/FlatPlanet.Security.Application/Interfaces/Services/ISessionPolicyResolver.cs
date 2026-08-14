using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

/// <summary>
/// The session timeouts and owning app to stamp onto a new session row.
/// </summary>
/// <param name="AppId">App the session belongs to, or null when the caller did not identify one.</param>
/// <param name="AbsoluteTimeoutMinutes">Value used for sessions.expires_at.</param>
/// <param name="IdleTimeoutMinutes">Value used for sessions.idle_timeout_minutes.</param>
public readonly record struct SessionPolicy(Guid? AppId, int AbsoluteTimeoutMinutes, int IdleTimeoutMinutes);

/// <summary>
/// Resolves the session policy for a login, applying per-app overrides
/// (apps.session_absolute_timeout_minutes / apps.session_idle_timeout_minutes)
/// on top of the platform defaults in security_config.
/// </summary>
public interface ISessionPolicyResolver
{
    /// <summary>
    /// Resolves by app slug. An unknown or absent slug falls back to the platform
    /// defaults with a null AppId — it never throws, because login has historically
    /// accepted any appSlug and rejecting one here would break existing clients.
    /// </summary>
    Task<SessionPolicy> ResolveAsync(string? appSlug, IReadOnlyDictionary<string, string> config);

    /// <summary>Resolves from an app the caller has already loaded and validated.</summary>
    SessionPolicy Resolve(App? app, IReadOnlyDictionary<string, string> config);
}
