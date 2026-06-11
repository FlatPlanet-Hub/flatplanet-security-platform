using System.Security.Claims;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace FlatPlanet.Security.API.Authorization;

/// <summary>
/// Resolves a ScopeRequirement against the caller's claims.
///
/// Pairs with the existing role policies (AdminAccess / PlatformOwner) which run
/// alongside [RequireScope]. The role policy gates user JWTs; this handler gates
/// service tokens by scope. User JWTs bypass scope checks entirely — their access
/// is governed by the role policy.
///
/// Pass conditions:
///   • Caller is a user (JwtBearer): scope check is N/A — succeed and let the
///     role policy enforce user-level requirements.
///   • Caller is a service token with the exact scope claim.
///   • Caller is a service token with the `bootstrap` wildcard scope claim.
/// </summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        var user = context.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
            return Task.CompletedTask;

        if (!user.IsInRole("service_token"))
        {
            // User JWT — role policy handles their authorization separately.
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var scopes = user.FindAll("scope").Select(c => c.Value).ToArray();
        if (scopes.Contains(ServiceToken.BootstrapScope, StringComparer.OrdinalIgnoreCase) ||
            scopes.Contains(requirement.Scope, StringComparer.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
