using System.IdentityModel.Tokens.Jwt;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Options;

namespace FlatPlanet.Security.Tests;

public class JwtServiceTests
{
    private JwtService CreateService(string secret = "super-secret-key-for-testing-purposes-only-32chars")
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SecretKey = secret,
            AccessTokenExpiryMinutes = 60
        });
        return new JwtService(options);
    }

    [Fact]
    public void IssueToken_ShouldContainCorrectClaims()
    {
        // Arrange
        var service = CreateService();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "user@test.com",
            FullName = "Test User",
            CompanyId = companyId,
            Status = "active"
        };

        // Act
        var token = service.IssueAccessToken(user);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);

        Assert.Equal(userId.ToString(), parsed.Subject);
        Assert.Equal("user@test.com", parsed.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(companyId.ToString(), parsed.Claims.First(c => c.Type == "company_id").Value);
        Assert.Equal("test-issuer", parsed.Issuer);
        Assert.Contains("test-audience", parsed.Audiences);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnHashedToken()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (plain, hash) = service.GenerateRefreshToken();

        // Assert
        Assert.NotEmpty(plain);
        Assert.NotEmpty(hash);
        Assert.NotEqual(plain, hash);

        // Hash should be consistent
        var reHash = service.HashToken(plain);
        Assert.Equal(hash, reHash);
    }
}
