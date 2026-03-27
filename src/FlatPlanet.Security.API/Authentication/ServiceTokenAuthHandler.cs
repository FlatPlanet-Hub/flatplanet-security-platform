using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using FlatPlanet.Security.Application.Common.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Security.API.Authentication;

public sealed class ServiceTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ServiceTokenOptions _serviceTokenOptions;

    public ServiceTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ServiceTokenOptions> serviceTokenOptions)
        : base(options, logger, encoder)
    {
        _serviceTokenOptions = serviceTokenOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Only activate if a service token is configured
        if (string.IsNullOrWhiteSpace(_serviceTokenOptions.Token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Constant-time comparison to prevent timing attacks
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var expectedBytes = Encoding.UTF8.GetBytes(_serviceTokenOptions.Token);
        if (!CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes))
            return Task.FromResult(AuthenticateResult.NoResult());

        // Use a well-known sentinel Guid so GetUserId() (Guid.Parse) succeeds on service-token calls.
        // This Guid is a fixed identity for all server-to-server requests from HubApi.
        const string serviceIdentityId = "00000000-0000-0000-0000-000000000001";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, serviceIdentityId),
            new Claim(ClaimTypes.Name, "service"),
            new Claim(ClaimTypes.Role, "platform_owner"),
            new Claim(ClaimTypes.Role, "app_admin"),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
