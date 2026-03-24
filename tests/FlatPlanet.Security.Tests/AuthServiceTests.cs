using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Moq;

namespace FlatPlanet.Security.Tests;

public class AuthServiceTests
{
    private readonly Mock<ISupabaseAuthClient> _supabaseAuth = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<ILoginAttemptRepository> _loginAttempts = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();
    private readonly Mock<ISecurityConfigRepository> _securityConfig = new();

    private AuthService CreateService() => new(
        _supabaseAuth.Object, _jwt.Object, _users.Object,
        _sessions.Object, _refreshTokens.Object,
        _loginAttempts.Object, _auditLog.Object, _securityConfig.Object);

    private void SetupDefaultConfig()
    {
        _securityConfig.Setup(s => s.GetIntValueAsync("rate_limit_login_per_ip_per_minute", 5)).ReturnsAsync(5);
        _securityConfig.Setup(s => s.GetIntValueAsync("max_failed_login_attempts", 5)).ReturnsAsync(5);
        _securityConfig.Setup(s => s.GetIntValueAsync("lockout_duration_minutes", 30)).ReturnsAsync(30);
        _securityConfig.Setup(s => s.GetIntValueAsync("max_concurrent_sessions", 3)).ReturnsAsync(3);
        _securityConfig.Setup(s => s.GetIntValueAsync("session_idle_timeout_minutes", 30)).ReturnsAsync(30);
        _securityConfig.Setup(s => s.GetIntValueAsync("session_absolute_timeout_minutes", 480)).ReturnsAsync(480);
        _securityConfig.Setup(s => s.GetIntValueAsync("jwt_refresh_expiry_days", 7)).ReturnsAsync(7);
        _securityConfig.Setup(s => s.GetIntValueAsync("jwt_access_expiry_minutes", 60)).ReturnsAsync(60);
    }

    [Fact]
    public async Task Login_ShouldReturnTokens_WhenCredentialsValid()
    {
        // Arrange
        SetupDefaultConfig();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        _loginAttempts.Setup(l => l.CountRecentByIpAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _loginAttempts.Setup(l => l.CountRecentFailuresByEmailAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _loginAttempts.Setup(l => l.RecordAsync(It.IsAny<LoginAttempt>())).Returns(Task.CompletedTask);

        _supabaseAuth.Setup(s => s.SignInAsync("user@test.com", "pass123"))
            .ReturnsAsync(new SupabaseAuthResult { UserId = userId, Email = "user@test.com" });

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, CompanyId = companyId, Email = "user@test.com", FullName = "Test User", Status = "active" });

        _sessions.Setup(s => s.CountActiveByUserAsync(userId)).ReturnsAsync(0);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>()))
            .ReturnsAsync((Session s) => { s.Id = Guid.NewGuid(); return s; });

        _jwt.Setup(j => j.IssueAccessToken(It.IsAny<User>())).Returns("access.token.here");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-token", "hashed-token"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>()))
            .ReturnsAsync((RefreshToken t) => t);

        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateLastSeenAtAsync(userId, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.LoginAsync(new LoginRequest { Email = "user@test.com", Password = "pass123" }, "1.2.3.4", "TestAgent");

        // Assert
        Assert.Equal("access.token.here", result.AccessToken);
        Assert.Equal("plain-token", result.RefreshToken);
        Assert.Equal(userId, result.User.UserId);
    }

    [Fact]
    public async Task Login_ShouldReturn401_WhenSupabaseAuthFails()
    {
        // Arrange
        SetupDefaultConfig();
        _loginAttempts.Setup(l => l.CountRecentByIpAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _loginAttempts.Setup(l => l.CountRecentFailuresByEmailAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _loginAttempts.Setup(l => l.RecordAsync(It.IsAny<LoginAttempt>())).Returns(Task.CompletedTask);
        _supabaseAuth.Setup(s => s.SignInAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((SupabaseAuthResult?)null);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest { Email = "bad@test.com", Password = "wrong" }, null, null));
    }

    [Fact]
    public async Task Login_ShouldReturn423_WhenAccountLocked()
    {
        // Arrange
        SetupDefaultConfig();
        _loginAttempts.Setup(l => l.CountRecentByIpAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        // 5 recent failures = locked
        _loginAttempts.Setup(l => l.CountRecentFailuresByEmailAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(5);

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<AccountLockedException>(() =>
            service.LoginAsync(new LoginRequest { Email = "locked@test.com", Password = "pass" }, null, null));
    }

    [Fact]
    public async Task Login_ShouldReturn429_WhenRateLimitExceeded()
    {
        // Arrange
        SetupDefaultConfig();
        // IP has 5 attempts (equals limit)
        _loginAttempts.Setup(l => l.CountRecentByIpAsync("1.2.3.4", It.IsAny<DateTime>())).ReturnsAsync(5);

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            service.LoginAsync(new LoginRequest { Email = "user@test.com", Password = "pass" }, "1.2.3.4", null));
    }

    [Fact]
    public async Task Login_ShouldReturn403_WhenUserInactive()
    {
        // Arrange
        SetupDefaultConfig();
        var userId = Guid.NewGuid();

        _loginAttempts.Setup(l => l.CountRecentByIpAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _loginAttempts.Setup(l => l.CountRecentFailuresByEmailAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(0);
        _supabaseAuth.Setup(s => s.SignInAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SupabaseAuthResult { UserId = userId });
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Status = "inactive" });

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.LoginAsync(new LoginRequest { Email = "user@test.com", Password = "pass" }, null, null));
    }

    [Fact]
    public async Task Logout_ShouldRevokeSessionAndToken()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _sessions.Setup(s => s.EndSessionAsync(sessionId, "logout")).Returns(Task.CompletedTask);
        _refreshTokens.Setup(r => r.RevokeAllByUserAsync(userId, "logout")).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.LogoutAsync(sessionId, userId, "1.2.3.4");

        // Assert
        _sessions.Verify(s => s.EndSessionAsync(sessionId, "logout"), Times.Once);
        _refreshTokens.Verify(r => r.RevokeAllByUserAsync(userId, "logout"), Times.Once);
    }

    [Fact]
    public async Task Refresh_ShouldRotateToken_WhenValid()
    {
        // Arrange
        SetupDefaultConfig();
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();

        _jwt.Setup(j => j.HashToken("valid-token")).Returns("valid-hash");
        _refreshTokens.Setup(r => r.GetByTokenHashAsync("valid-hash"))
            .ReturnsAsync(new RefreshToken
            {
                Id = tokenId,
                UserId = userId,
                TokenHash = "valid-hash",
                Revoked = false,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", FullName = "Test", Status = "active" });
        _refreshTokens.Setup(r => r.RevokeAsync(tokenId, "rotated")).Returns(Task.CompletedTask);
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("new-plain", "new-hash"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>()))
            .ReturnsAsync((RefreshToken t) => t);
        _jwt.Setup(j => j.IssueAccessToken(It.IsAny<User>())).Returns("new-access-token");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.RefreshAsync(new RefreshRequest { RefreshToken = "valid-token" }, null);

        // Assert
        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal("new-plain", result.RefreshToken);
        _refreshTokens.Verify(r => r.RevokeAsync(tokenId, "rotated"), Times.Once);
    }

    [Fact]
    public async Task Refresh_ShouldFail_WhenTokenRevoked()
    {
        // Arrange
        _jwt.Setup(j => j.HashToken("revoked-token")).Returns("revoked-hash");
        _refreshTokens.Setup(r => r.GetByTokenHashAsync("revoked-hash"))
            .ReturnsAsync(new RefreshToken
            {
                Id = Guid.NewGuid(),
                Revoked = true,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });

        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RefreshAsync(new RefreshRequest { RefreshToken = "revoked-token" }, null));
    }
}
