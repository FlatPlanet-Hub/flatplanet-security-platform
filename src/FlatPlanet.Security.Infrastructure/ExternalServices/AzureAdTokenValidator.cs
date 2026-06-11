using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlatPlanet.Security.Infrastructure.ExternalServices;

public class AzureAdTokenValidator : IAzureAdTokenValidator
{
    private readonly AzureAdOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    private const string JwksCacheKey = "fp:sec:azure_ad_jwks";

    public AzureAdTokenValidator(
        IOptions<AzureAdOptions> options,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _options           = options.Value;
        _httpClientFactory = httpClientFactory;
        _cache             = cache;
    }

    public async Task<ClaimsPrincipal> ValidateAsync(string idToken)
    {
        var signingKeys = await GetSigningKeysAsync();
        var handler     = new JwtSecurityTokenHandler();
        var parameters  = BuildValidationParameters(signingKeys);

        try
        {
            return await ValidateWithKeyRotationAsync(handler, parameters, idToken);
        }
        catch (SecurityTokenException ex)
        {
            throw new TokenValidationException(ex.Message, ex);
        }
    }

    private async Task<ClaimsPrincipal> ValidateWithKeyRotationAsync(
        JwtSecurityTokenHandler handler,
        TokenValidationParameters parameters,
        string idToken)
    {
        try
        {
            return handler.ValidateToken(idToken, parameters, out _);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Key rotation: evict stale cache and retry once with fresh JWKS
            _cache.Remove(JwksCacheKey);
            parameters.IssuerSigningKeys = await GetSigningKeysAsync();
            return handler.ValidateToken(idToken, parameters, out _);
        }
    }

    private TokenValidationParameters BuildValidationParameters(IEnumerable<SecurityKey> signingKeys) =>
        new TokenValidationParameters
        {
            ValidateIssuer           = true,
            // Single-tenant only — multi-tenant / personal MS accounts intentionally not supported.
            ValidIssuer              = $"https://login.microsoftonline.com/{_options.TenantId}/v2.0",
            ValidateAudience         = true,
            ValidAudience            = _options.ClientId,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = signingKeys,
            // Small clock-skew to tolerate minor clock drift between Azure and SP host
            ClockSkew                = TimeSpan.FromMinutes(5)
        };

    private async Task<IEnumerable<SecurityKey>> GetSigningKeysAsync()
    {
        if (_cache.TryGetValue(JwksCacheKey, out IEnumerable<SecurityKey>? cached) && cached != null)
            return cached;

        var client  = _httpClientFactory.CreateClient("AzureAdJwks");
        var jwksUrl = $"https://login.microsoftonline.com/{_options.TenantId}/discovery/v2.0/keys";
        var json    = await client.GetStringAsync(jwksUrl);

        var keySet = new JsonWebKeySet(json);
        var keys   = keySet.GetSigningKeys();

        _cache.Set(JwksCacheKey, keys, TimeSpan.FromHours(24));
        return keys;
    }
}
