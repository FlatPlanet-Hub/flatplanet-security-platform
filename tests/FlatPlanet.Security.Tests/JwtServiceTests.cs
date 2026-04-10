using System.IdentityModel.Tokens.Jwt;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Options;
using Moq;

namespace FlatPlanet.Security.Tests;

public class JwtServiceTests
{
    private JwtService CreateService(
        string secret = "super-secret-key-for-testing-purposes-only-32chars",
        IBusinessMembershipRepository? membershipRepo = null)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SecretKey = secret,
            AccessTokenExpiryMinutes = 60
        });

        var repo = membershipRepo ?? CreateEmptyMembershipRepo();
        return new JwtService(options, repo);
    }

    private static IBusinessMembershipRepository CreateEmptyMembershipRepo()
    {
        var mock = new Mock<IBusinessMembershipRepository>();
        mock.Setup(r => r.GetActiveByUserIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<UserBusinessMembership>());
        return mock.Object;
    }

    [Fact]
    public async Task IssueToken_ShouldContainCorrectClaims()
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

        var sessionId = Guid.NewGuid();

        // Act
        var token = await service.IssueAccessTokenAsync(user, sessionId, new[] { "platform_owner" });

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
    public async Task IssueToken_ShouldContainBusinessCodesClaims_WhenMembershipsExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var memberships = new List<UserBusinessMembership>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, CompanyId = Guid.NewGuid(), BusinessCode = "fp", BusinessName = "FlatPlanet" },
            new() { Id = Guid.NewGuid(), UserId = userId, CompanyId = Guid.NewGuid(), BusinessCode = "why_you", BusinessName = "Why You Co" }
        };

        var mock = new Mock<IBusinessMembershipRepository>();
        mock.Setup(r => r.GetActiveByUserIdAsync(userId)).ReturnsAsync(memberships);

        var service = CreateService(membershipRepo: mock.Object);
        var user = new User
        {
            Id = userId,
            Email = "user@test.com",
            FullName = "Test User",
            CompanyId = Guid.NewGuid(),
            Status = "active"
        };

        // Act
        var token = await service.IssueAccessTokenAsync(user, Guid.NewGuid(), Array.Empty<string>());

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);
        var businessCodeClaims = parsed.Claims
            .Where(c => c.Type == "business_codes")
            .Select(c => c.Value)
            .ToList();

        Assert.Contains("fp", businessCodeClaims);
        Assert.Contains("why_you", businessCodeClaims);
        Assert.Equal(2, businessCodeClaims.Count);
    }

    [Fact]
    public async Task IssueToken_ShouldNotContainBusinessCodesClaims_WhenNoMemberships()
    {
        // Arrange
        var service = CreateService(); // uses empty repo by default
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "solo@test.com",
            FullName = "Solo User",
            CompanyId = Guid.NewGuid(),
            Status = "active"
        };

        // Act
        var token = await service.IssueAccessTokenAsync(user, Guid.NewGuid(), Array.Empty<string>());

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);
        Assert.DoesNotContain(parsed.Claims, c => c.Type == "business_codes");
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
