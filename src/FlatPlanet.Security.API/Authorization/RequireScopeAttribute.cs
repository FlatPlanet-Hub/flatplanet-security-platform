using Microsoft.AspNetCore.Authorization;

namespace FlatPlanet.Security.API.Authorization;

/// <summary>
/// Requires that a service-token caller hold a specific scope, OR that the caller
/// is a user (JWT). User JWTs are not gated by scopes — their access is controlled
/// by existing role policies (AdminAccess, PlatformOwner) which run alongside.
///
/// Pair with an existing [Authorize] policy to combine: the policy gates role,
/// and this attribute additionally narrows service-token callers to a scope.
///
/// Bootstrap-scoped tokens satisfy any scope check (see ServiceToken.BootstrapScope).
/// </summary>
public sealed class RequireScopeAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "scope:";

    public RequireScopeAttribute(string scope)
    {
        Policy = PolicyPrefix + scope;
    }
}

/// <summary>
/// Requirement encapsulating the scope string. The handler reads it back out and
/// checks the caller's claims.
/// </summary>
public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public string Scope { get; }
    public ScopeRequirement(string scope) => Scope = scope;
}

/// <summary>
/// Dynamic policy provider: builds a one-off policy for each RequireScopeAttribute
/// invocation. Avoids the need to pre-register every possible scope in Program.cs.
/// </summary>
public sealed class ScopePolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public ScopePolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequireScopeAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var scope = policyName[RequireScopeAttribute.PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes("ServiceToken", Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
                .AddRequirements(new ScopeRequirement(scope))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}
