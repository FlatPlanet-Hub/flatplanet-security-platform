using System.Security.Claims;
using FlatPlanet.Security.API.Authorization;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace FlatPlanet.Security.Tests;

public class ScopeAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext Build(ClaimsPrincipal user, string requiredScope)
    {
        var requirement = new ScopeRequirement(requiredScope);
        return new AuthorizationHandlerContext(new[] { requirement }, user, null);
    }

    private static ClaimsPrincipal ServiceTokenPrincipal(params string[] scopes)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, "service_token") };
        foreach (var s in scopes) claims.Add(new Claim("scope", s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ServiceToken"));
    }

    private static ClaimsPrincipal UserJwt(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    [Fact]
    public async Task UserJwt_BypassesScopeCheck_RegardlessOfScopes()
    {
        var handler = new ScopeAuthorizationHandler();
        var ctx = Build(UserJwt("app_admin"), "users:write");

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ServiceToken_WithExactScope_Succeeds()
    {
        var handler = new ScopeAuthorizationHandler();
        var ctx = Build(ServiceTokenPrincipal("users:write", "roles:read"), "users:write");

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ServiceToken_WithBootstrap_SucceedsForAnyScope()
    {
        var handler = new ScopeAuthorizationHandler();
        var ctx = Build(ServiceTokenPrincipal(ServiceToken.BootstrapScope), "audit:read");

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ServiceToken_MissingScope_Fails()
    {
        var handler = new ScopeAuthorizationHandler();
        var ctx = Build(ServiceTokenPrincipal("users:read"), "users:write");

        await handler.HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task ServiceToken_ScopeMatchIsCaseInsensitive()
    {
        var handler = new ScopeAuthorizationHandler();
        var ctx = Build(ServiceTokenPrincipal("USERS:WRITE"), "users:write");

        await handler.HandleAsync(ctx);

        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Unauthenticated_Fails()
    {
        var handler = new ScopeAuthorizationHandler();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no AuthenticationType → not authenticated
        var ctx = Build(anonymous, "users:read");

        await handler.HandleAsync(ctx);

        Assert.False(ctx.HasSucceeded);
    }
}
