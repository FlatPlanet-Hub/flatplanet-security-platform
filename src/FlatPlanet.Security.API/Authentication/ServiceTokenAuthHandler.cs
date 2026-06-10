using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Security.API.Authentication;

/// <summary>
/// Authenticates server-to-server requests.
///
/// Resolution order:
///   1. DB-backed per-service token (preferred) — see service_tokens table.
///      Returns claims: NameIdentifier = service token id, Name = service name,
///      Role = "service_token" (NOT platform_owner/app_admin), plus a "scope" claim per scope.
///   2. Legacy single shared token from appsettings (`ServiceToken:Token`) — kept for
///      backward compatibility during Phase 1 of the per-service token migration.
///      Returns the original platform_owner + app_admin claims so existing admin
///      policies keep working until callers are migrated.
///
/// On any miss the handler returns NoResult so other schemes (JwtBearer) can try.
/// </summary>
public sealed class ServiceTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ServiceTokenOptions _serviceTokenOptions;
    private readonly IServiceTokenService _serviceTokenService;

    // Sentinel identity for the legacy single-token path so GetUserId() (Guid.Parse) succeeds.
    private const string LegacyServiceIdentityId = "00000000-0000-0000-0000-000000000001";

    public ServiceTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ServiceTokenOptions> serviceTokenOptions,
        IServiceTokenService serviceTokenService)
        : base(options, logger, encoder)
    {
        _serviceTokenOptions = serviceTokenOptions.Value;
        _serviceTokenService = serviceTokenService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        // ── 1. DB-backed per-service token (preferred path) ─────────────────
        var dbToken = await _serviceTokenService.ValidateAsync(token);
        if (dbToken is not null)
            return AuthenticateResult.Success(BuildPerServiceTicket(dbToken));

        // ── 2. Legacy single-token fallback (Phase 1 compat) ────────────────
        if (!string.IsNullOrWhiteSpace(_serviceTokenOptions.Token))
        {
            var tokenBytes = Encoding.UTF8.GetBytes(token);
            var expectedBytes = Encoding.UTF8.GetBytes(_serviceTokenOptions.Token);
            if (CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes))
                return AuthenticateResult.Success(BuildLegacyTicket());
        }

        return AuthenticateResult.NoResult();
    }

    private AuthenticationTicket BuildPerServiceTicket(ServiceToken token)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.Id.ToString()),
            new(ClaimTypes.Name, token.ServiceName),
            new(ClaimTypes.Role, "service_token"),
            new("service_name", token.ServiceName),
        };
        foreach (var scope in token.Scopes)
            claims.Add(new Claim("scope", scope));

        // Bootstrap-scoped tokens also receive the legacy admin roles so they
        // can pass the existing [Authorize(Policy="AdminAccess"/"PlatformOwner")]
        // checks. This is the compatibility bridge for Phase 2 — when HubApi
        // first switches to a per-service token, it gets bootstrap scope and
        // continues to satisfy the existing admin policies untouched.
        // In Phase 3 (separate work), scopes are narrowed and admin endpoints
        // gain [RequireScope(...)] annotations; bootstrap stops being the
        // mechanism by which admin access is granted.
        if (token.Scopes.Contains(ServiceToken.BootstrapScope, StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, "platform_owner"));
            claims.Add(new Claim(ClaimTypes.Role, "app_admin"));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
    }

    private AuthenticationTicket BuildLegacyTicket()
    {
        var serviceName = Request.Headers.TryGetValue("X-Service-Name", out var sn)
            ? sn.ToString()
            : "unknown";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, LegacyServiceIdentityId),
            new Claim(ClaimTypes.Name, "service"),
            new Claim(ClaimTypes.Role, "platform_owner"),
            new Claim(ClaimTypes.Role, "app_admin"),
            new Claim("service_name", serviceName),
            new Claim("scope", ServiceToken.BootstrapScope),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
    }
}
