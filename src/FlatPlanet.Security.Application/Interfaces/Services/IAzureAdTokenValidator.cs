using System.Security.Claims;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IAzureAdTokenValidator
{
    /// <summary>
    /// Validates an Azure AD id_token against Microsoft's JWKS endpoint.
    /// Returns the validated claims principal on success.
    /// Throws <see cref="Common.Exceptions.TokenValidationException"/> when the token is invalid or expired.
    /// Non-token errors (e.g. network failure fetching JWKS) propagate as-is.
    /// </summary>
    Task<ClaimsPrincipal> ValidateAsync(string idToken);
}
