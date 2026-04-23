using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlatPlanet.Security.Application.Services;

public class JwtService : IJwtService
{
    private readonly JwtOptions _options;
    private readonly IBusinessMembershipRepository _businessMembershipRepo;

    public JwtService(IOptions<JwtOptions> options, IBusinessMembershipRepository businessMembershipRepo)
    {
        _options = options.Value;
        _businessMembershipRepo = businessMembershipRepo;
    }

    public async Task<string> IssueAccessTokenAsync(User user, Guid sessionId, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("full_name", user.FullName),
            new Claim("company_id", user.CompanyId.ToString()),
            new Claim("session_id", sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var memberships = await _businessMembershipRepo.GetActiveByUserIdAsync(user.Id);
        foreach (var m in memberships.Where(m => m.BusinessCode != null))
        {
            claims.Add(new Claim("business_codes", m.BusinessCode!));
            claims.Add(new Claim("business_ids", m.CompanyId.ToString()));
        }

        return BuildToken(claims, _options.AccessTokenExpiryMinutes);
    }

    public async Task<string> IssueEnrolmentTokenAsync(User user, Guid sessionId)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("full_name", user.FullName),
            new Claim("company_id", user.CompanyId.ToString()),
            new Claim("session_id", sessionId.ToString()),
            new Claim("enrolment_only", "true"),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        // No roles, no business memberships — user has not yet proven MFA
        // Suppress CS1998 — kept async to match the interface signature
        await Task.CompletedTask;
        return BuildToken(claims, expiryMinutes: 10);
    }

    private string BuildToken(IEnumerable<Claim> claims, int expiryMinutes)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string token, string hash) GenerateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = HashToken(token);
        return (token, hash);
    }

    public string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
