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
    /// Resolves by app slug for a login where the app has NOT yet been authorised.
    /// </summary>
    /// <remarks>
    /// The slug arrives as unauthenticated client input on anonymous endpoints, so an
    /// override is applied only when the app is active AND the user holds an active role
    /// grant to it. Without that check any account could claim another app's session
    /// lifetime just by sending its slug.
    ///
    /// Never throws: an unknown slug, an inactive app, or a missing grant falls back to
    /// the platform defaults and logs a warning. Login has always accepted an arbitrary
    /// appSlug (see docs/security-api-reference.md), so rejecting one here would break
    /// existing clients that send a stale value.
    /// </remarks>
    Task<SessionPolicy> ResolveAsync(Guid userId, string? appSlug, IReadOnlyDictionary<string, string> config);

    /// <summary>
    /// Resolves from an app the caller has ALREADY authorised for this user.
    /// </summary>
    /// <remarks>
    /// Applies the app's override unconditionally — it performs no status or grant check.
    /// Only call this after verifying both, as FederatedLoginService does before creating
    /// its session. If you have not verified access, call <see cref="ResolveAsync"/>.
    /// </remarks>
    SessionPolicy ResolveForAuthorisedApp(App? app, IReadOnlyDictionary<string, string> config);
}
