using System.Data;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace FlatPlanet.Security.Tests;

public class MfaServiceTests
{
    private readonly Mock<IMfaChallengeRepository> _challenges = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ISecurityConfigRepository> _securityConfig = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IDbConnectionFactory> _db = new();
    private readonly Mock<IDbConnection> _conn = new();
    private readonly Mock<IDbTransaction> _tx = new();
    private readonly Mock<IIdentityVerificationService> _identityVerification = new();
    private readonly Mock<ITotpSecretEncryptor> _encryptor = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ILogger<MfaService>> _logger = new();

    private MfaService CreateService() => new(
        _challenges.Object, _users.Object, _email.Object,
        _securityConfig.Object, _jwt.Object, _auditLog.Object,
        _sessions.Object, _refreshTokens.Object, _roles.Object,
        _db.Object, _identityVerification.Object, _encryptor.Object,
        _cache, _logger.Object);

    private void SetupTransaction()
    {
        _conn.Setup(c => c.BeginTransaction()).Returns(_tx.Object);
        _db.Setup(d => d.CreateConnectionAsync()).ReturnsAsync(_conn.Object);
    }

    private void SetupDefaultSessionConfig()
    {
        _securityConfig.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<SecurityConfig>
        {
            new() { ConfigKey = "max_concurrent_sessions", ConfigValue = "3" },
            new() { ConfigKey = "session_absolute_timeout_minutes", ConfigValue = "480" },
            new() { ConfigKey = "session_idle_timeout_minutes", ConfigValue = "30" },
            new() { ConfigKey = "jwt_refresh_expiry_days", ConfigValue = "7" },
            new() { ConfigKey = "jwt_access_expiry_minutes", ConfigValue = "60" },
        });
    }

    // ── BeginTotpEnrolmentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task BeginTotpEnrolment_ShouldReturnQrUri_WhenNotEnrolled()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", MfaTotpEnrolled = false });
        _securityConfig.Setup(s => s.GetValueAsync("mfa_totp_issuer")).ReturnsAsync("FlatPlanet");
        _encryptor.Setup(e => e.Encrypt(It.IsAny<byte[]>())).Returns("encrypted-secret");
        _users.Setup(u => u.UpdateMfaTotpSecretAsync(userId, "encrypted-secret")).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.BeginTotpEnrolmentAsync(userId);

        Assert.Contains("otpauth://totp/", result.QrCodeUri);
        Assert.Contains("FlatPlanet", result.QrCodeUri);
        Assert.Contains("user@test.com", Uri.UnescapeDataString(result.QrCodeUri));
    }

    [Fact]
    public async Task BeginTotpEnrolment_ShouldThrow_WhenAlreadyEnrolled()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", MfaTotpEnrolled = true });

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BeginTotpEnrolmentAsync(userId));
    }

    [Fact]
    public async Task BeginTotpEnrolment_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.BeginTotpEnrolmentAsync(userId));
    }

    // ── SendEmailOtpAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SendEmailOtp_ShouldCreateChallengeAndSendEmail()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com" });
        _securityConfig.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<SecurityConfig>
        {
            new() { ConfigKey = "mfa_email_otp_expiry_minutes", ConfigValue = "10" },
            new() { ConfigKey = "mfa_otp_length", ConfigValue = "6" },
        });
        _challenges.Setup(c => c.InvalidateActiveByTypeAsync(userId, "email_otp")).Returns(Task.CompletedTask);
        _jwt.Setup(j => j.HashToken(It.IsAny<string>())).Returns("otp-hash");
        _challenges.Setup(c => c.CreateAsync(It.IsAny<MfaChallenge>()))
            .ReturnsAsync((MfaChallenge ch) => { ch.Id = Guid.NewGuid(); return ch; });
        _email.Setup(e => e.SendMfaOtpEmailAsync("user@test.com", It.IsAny<string>(), 10)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.SendEmailOtpAsync(userId, "1.2.3.4");

        Assert.Equal("email_otp", result.ChallengeType);
        Assert.Equal("user@test.com", result.Email);
        _email.Verify(e => e.SendMfaOtpEmailAsync("user@test.com", It.IsAny<string>(), 10), Times.Once);
    }

    [Fact]
    public async Task SendEmailOtp_ShouldThrow503_WhenEmailFails()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com" });
        _securityConfig.Setup(s => s.GetAllAsync()).ReturnsAsync(new List<SecurityConfig>
        {
            new() { ConfigKey = "mfa_email_otp_expiry_minutes", ConfigValue = "10" },
            new() { ConfigKey = "mfa_otp_length", ConfigValue = "6" },
        });
        _challenges.Setup(c => c.InvalidateActiveByTypeAsync(userId, "email_otp")).Returns(Task.CompletedTask);
        _jwt.Setup(j => j.HashToken(It.IsAny<string>())).Returns("otp-hash");
        _challenges.Setup(c => c.CreateAsync(It.IsAny<MfaChallenge>()))
            .ReturnsAsync((MfaChallenge ch) => { ch.Id = Guid.NewGuid(); return ch; });
        _email.Setup(e => e.SendMfaOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("SMTP down"));

        var svc = CreateService();
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => svc.SendEmailOtpAsync(userId, null));
    }

    [Fact]
    public async Task SendEmailOtp_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.SendEmailOtpAsync(userId, null));
    }

    // ── VerifyLoginEmailOtpAsync ─────────────────────────────────────────────

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldReturnTokens_WhenOtpValid()
    {
        SetupTransaction();
        SetupDefaultSessionConfig();

        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = userId, ChallengeType = "email_otp",
            OtpHash = "correct-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 0
        });
        _securityConfig.Setup(s => s.GetValueAsync("mfa_max_otp_attempts")).ReturnsAsync("5");
        _jwt.Setup(j => j.HashToken("123456")).Returns("correct-hash");
        _challenges.Setup(c => c.MarkVerifiedAsync(challengeId)).Returns(Task.CompletedTask);
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", FullName = "Test", CompanyId = Guid.NewGuid() });

        _sessions.Setup(s => s.EvictOldestIfOverLimitAsync(userId, It.IsAny<int>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((Session s, IDbConnection _, IDbTransaction _) => { s.Id = sessionId; return s; });
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-rt", "hashed-rt"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((RefreshToken t, IDbConnection _, IDbTransaction _) => t);
        _roles.Setup(r => r.GetPlatformRoleNamesForUserAsync(userId)).ReturnsAsync(new List<string>());
        _jwt.Setup(j => j.IssueAccessTokenAsync(It.IsAny<User>(), sessionId, It.IsAny<IEnumerable<string>>())).ReturnsAsync("access-token");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.VerifyLoginEmailOtpAsync(challengeId, "123456", "1.2.3.4", "agent");

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("plain-rt", result.RefreshToken);
        Assert.Equal(userId, result.User.UserId);
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenOtpInvalid()
    {
        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = userId, ChallengeType = "email_otp",
            OtpHash = "correct-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 0
        });
        _securityConfig.Setup(s => s.GetValueAsync("mfa_max_otp_attempts")).ReturnsAsync("5");
        _jwt.Setup(j => j.HashToken("wrong")).Returns("wrong-hash");
        _challenges.Setup(c => c.IncrementAttemptsAsync(challengeId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "wrong", null, null));
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenChallengeExpired()
    {
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = Guid.NewGuid(), ChallengeType = "email_otp",
            OtpHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(-1), Attempts = 0
        });
        _securityConfig.Setup(s => s.GetValueAsync("mfa_max_otp_attempts")).ReturnsAsync("5");

        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenMaxAttemptsExceeded()
    {
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = Guid.NewGuid(), ChallengeType = "email_otp",
            OtpHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 5
        });
        _securityConfig.Setup(s => s.GetValueAsync("mfa_max_otp_attempts")).ReturnsAsync("5");

        var svc = CreateService();
        await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenChallengeTypeWrong()
    {
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = Guid.NewGuid(), ChallengeType = "totp_enrollment",
            OtpHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 0
        });
        _securityConfig.Setup(s => s.GetValueAsync("mfa_max_otp_attempts")).ReturnsAsync("5");

        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "123456", null, null));
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisableMfa_ShouldCallResetColumns()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.ResetMfaColumnsAsync(userId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await svc.DisableMfaAsync(userId);

        _users.Verify(u => u.ResetMfaColumnsAsync(userId), Times.Once);
    }

    [Fact]
    public async Task ResetMfa_ShouldCallResetColumns()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.ResetMfaColumnsAsync(userId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await svc.ResetMfaAsync(userId);

        _users.Verify(u => u.ResetMfaColumnsAsync(userId), Times.Once);
    }
}
