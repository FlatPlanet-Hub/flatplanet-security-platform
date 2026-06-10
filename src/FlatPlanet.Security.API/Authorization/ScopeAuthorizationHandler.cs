using System.Security.Claims;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace FlatPlanet.Security.API.Authorization;

/// <summary>
/// Resolves a ScopeRequirement against the caller's claims.
///
/// Pass conditions (any of these):
///   • Caller is a user (JwtBearer): scope checks do not apply — user authorization
///     is governed by role policies elsewhere. We succeed and let the policy chain
///     continue to enforce role requirements.
///   • Caller is a service token with the exact scope.
///   • Caller is a service token with the `bootstrap` wildcard scope.
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

        // Users (JWT) bypass scope checks — role policies handle them separately.
        if (!user.IsInRole("service_token"))
        {
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
