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
    private readonly Mock<ISecurityConfigService> _configService = new();
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
    private readonly Mock<ITotpVerifier> _totpVerifier = new();
    private readonly Mock<IMfaBackupCodeRepository> _backupCodes = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ILogger<MfaService>> _logger = new();

    private MfaService CreateService() => new(
        _challenges.Object, _users.Object, _email.Object,
        _configService.Object, _jwt.Object, _auditLog.Object,
        _sessions.Object, _refreshTokens.Object, _roles.Object,
        _db.Object, _identityVerification.Object, _encryptor.Object,
        _totpVerifier.Object, _backupCodes.Object, _cache, _logger.Object);

    private void SetupTransaction()
    {
        _conn.Setup(c => c.BeginTransaction()).Returns(_tx.Object);
        _db.Setup(d => d.CreateConnectionAsync()).ReturnsAsync(_conn.Object);
    }

    /// <summary>Sets up GetAllAsync with all config keys used by MfaService. Pass overrides to change specific values.</summary>
    private void SetupConfig(params (string key, string val)[] overrides)
    {
        var defaults = new Dictionary<string, string>
        {
            ["max_concurrent_sessions"]          = "3",
            ["session_absolute_timeout_minutes"] = "480",
            ["session_idle_timeout_minutes"]     = "30",
            ["jwt_refresh_expiry_days"]          = "7",
            ["jwt_access_expiry_minutes"]        = "60",
            ["mfa_max_otp_attempts"]             = "5",
            ["mfa_email_otp_expiry_minutes"]     = "10",
            ["mfa_otp_length"]                   = "6",
            ["mfa_totp_issuer"]                  = "FlatPlanet",
        };
        foreach (var (k, v) in overrides)
            defaults[k] = v;

        _configService.Setup(s => s.GetAllCachedAsync()).ReturnsAsync(defaults);
    }

    // ── BeginTotpEnrolmentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task BeginTotpEnrolment_ShouldReturnQrUri_WhenNotEnrolled()
    {
        var userId = Guid.NewGuid();
        SetupConfig();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", MfaTotpEnrolled = false });
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

    // ── VerifyTotpEnrolmentAsync ─────────────────────────────────────────────

    [Fact]
    public async Task VerifyTotpEnrolment_ShouldEnrolAndReturnTokens_WhenCodeValid()
    {
        SetupTransaction();
        SetupConfig();

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var secretBytes = new byte[20];
        long matchedStep = 100L;

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", FullName = "Test",
                CompanyId = Guid.NewGuid(), MfaTotpSecret = "encrypted", MfaTotpLastUsedStep = null });
        _encryptor.Setup(e => e.Decrypt("encrypted")).Returns(secretBytes);
        _totpVerifier.Setup(t => t.Verify(secretBytes, "123456", out matchedStep)).Returns(true);
        _users.Setup(u => u.CompleteTotpEnrolmentAsync(userId, matchedStep)).Returns(Task.CompletedTask);
        _identityVerification.Setup(iv => iv.SyncStatusAsync(userId, true)).Returns(Task.CompletedTask);

        _sessions.Setup(s => s.EvictOldestIfOverLimitAsync(userId, It.IsAny<int>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((Session s, IDbConnection _, IDbTransaction _) => { s.Id = sessionId; return s; });
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-rt", "hashed-rt"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((RefreshToken t, IDbConnection _, IDbTransaction _) => t);
        _roles.Setup(r => r.GetPlatformRoleNamesForUserAsync(userId)).ReturnsAsync(new List<string>());
        _jwt.Setup(j => j.IssueAccessTokenAsync(It.IsAny<User>(), sessionId, It.IsAny<IEnumerable<string>>())).ReturnsAsync("access-token");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateLastSeenAtAsync(userId, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.VerifyTotpEnrolmentAsync(userId, "123456", "1.2.3.4", "agent");

        Assert.Equal("access-token", result.AccessToken);
        Assert.True(result.MfaEnrolled);
        _users.Verify(u => u.CompleteTotpEnrolmentAsync(userId, matchedStep), Times.Once);
        _identityVerification.Verify(iv => iv.SyncStatusAsync(userId, true), Times.Once);
    }

    [Fact]
    public async Task VerifyTotpEnrolment_ShouldThrow_WhenNoSecretStarted()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpSecret = null });

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.VerifyTotpEnrolmentAsync(userId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyTotpEnrolment_ShouldThrow_WhenCodeInvalid()
    {
        var userId = Guid.NewGuid();
        var secretBytes = new byte[20];
        long matchedStep = 0L;

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpSecret = "encrypted" });
        _encryptor.Setup(e => e.Decrypt("encrypted")).Returns(secretBytes);
        _totpVerifier.Setup(t => t.Verify(secretBytes, "000000", out matchedStep)).Returns(false);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyTotpEnrolmentAsync(userId, "000000", null, null));
    }

    [Fact]
    public async Task VerifyTotpEnrolment_ShouldThrow_WhenStepReplayed()
    {
        var userId = Guid.NewGuid();
        var secretBytes = new byte[20];
        long matchedStep = 99L;

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpSecret = "encrypted", MfaTotpLastUsedStep = 99L });
        _encryptor.Setup(e => e.Decrypt("encrypted")).Returns(secretBytes);
        _totpVerifier.Setup(t => t.Verify(secretBytes, "123456", out matchedStep)).Returns(true);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyTotpEnrolmentAsync(userId, "123456", null, null));
    }

    // ── SendEmailOtpAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SendEmailOtp_ShouldCreateChallengeAndSendEmail()
    {
        var userId = Guid.NewGuid();
        SetupConfig();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", Status = "active" });
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
    public async Task SendEmailOtp_ShouldThrow_WhenUserSuspended()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Status = "suspended" });

        var svc = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SendEmailOtpAsync(userId, null));
    }

    [Fact]
    public async Task SendEmailOtp_ShouldThrow503_WhenEmailFails()
    {
        var userId = Guid.NewGuid();
        SetupConfig();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", Status = "active" });
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

    // ── ResendEmailOtpAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ResendEmailOtp_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ResendEmailOtpAsync(userId, null));
    }

    [Fact]
    public async Task ResendEmailOtp_ShouldThrow_WhenEmailOtpNotEnabled()
    {
        var userId = Guid.NewGuid();
        // TOTP user — email OTP not configured
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaEnabled = true, MfaMethod = "totp", Status = "active" });

        var svc = CreateService();
        // P2-02: Must return same error as user-not-found to prevent MFA method disclosure
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ResendEmailOtpAsync(userId, null));
    }

    [Fact]
    public async Task ResendEmailOtp_ShouldSendOtp_WhenEmailOtpEnabled()
    {
        var userId = Guid.NewGuid();
        SetupConfig();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", MfaEnabled = true, MfaMethod = "email_otp", Status = "active" });
        _challenges.Setup(c => c.InvalidateActiveByTypeAsync(userId, "email_otp")).Returns(Task.CompletedTask);
        _jwt.Setup(j => j.HashToken(It.IsAny<string>())).Returns("otp-hash");
        _challenges.Setup(c => c.CreateAsync(It.IsAny<MfaChallenge>()))
            .ReturnsAsync((MfaChallenge ch) => { ch.Id = Guid.NewGuid(); return ch; });
        _email.Setup(e => e.SendMfaOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.ResendEmailOtpAsync(userId, null);

        Assert.NotEqual(Guid.Empty, result.Id);
        _email.Verify(e => e.SendMfaOtpEmailAsync("user@test.com", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    // ── VerifyLoginEmailOtpAsync ─────────────────────────────────────────────

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldReturnTokens_WhenOtpValid()
    {
        SetupTransaction();
        SetupConfig();

        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = userId, ChallengeType = "email_otp",
            OtpHash = "correct-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 0
        });
        _jwt.Setup(j => j.HashToken("123456")).Returns("correct-hash");
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", FullName = "Test",
                CompanyId = Guid.NewGuid(), Status = "active" });
        _challenges.Setup(c => c.MarkVerifiedAsync(challengeId)).Returns(Task.CompletedTask);

        _sessions.Setup(s => s.EvictOldestIfOverLimitAsync(userId, It.IsAny<int>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((Session s, IDbConnection _, IDbTransaction _) => { s.Id = sessionId; return s; });
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-rt", "hashed-rt"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((RefreshToken t, IDbConnection _, IDbTransaction _) => t);
        _roles.Setup(r => r.GetPlatformRoleNamesForUserAsync(userId)).ReturnsAsync(new List<string>());
        _jwt.Setup(j => j.IssueAccessTokenAsync(It.IsAny<User>(), sessionId, It.IsAny<IEnumerable<string>>())).ReturnsAsync("access-token");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateLastSeenAtAsync(userId, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.VerifyLoginEmailOtpAsync(challengeId, "123456", "1.2.3.4", "agent");

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("plain-rt", result.RefreshToken);
        Assert.Equal(userId, result.User.UserId);
        // I-1: MarkVerifiedAsync must be called (challenge consumed only after checks pass)
        _challenges.Verify(c => c.MarkVerifiedAsync(challengeId), Times.Once);
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenOtpInvalid()
    {
        SetupConfig();
        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = userId, ChallengeType = "email_otp",
            OtpHash = "correct-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 0
        });
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
        SetupConfig();
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = Guid.NewGuid(), ChallengeType = "email_otp",
            OtpHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(-1), Attempts = 0
        });

        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenMaxAttemptsExceeded()
    {
        SetupConfig();
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = Guid.NewGuid(), ChallengeType = "email_otp",
            OtpHash = "hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 5
        });

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

        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyLoginEmailOtp_ShouldThrow_WhenUserSuspended()
    {
        SetupConfig();
        var userId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        _challenges.Setup(c => c.GetByIdAsync(challengeId)).ReturnsAsync(new MfaChallenge
        {
            Id = challengeId, UserId = userId, ChallengeType = "email_otp",
            OtpHash = "correct-hash", ExpiresAt = DateTime.UtcNow.AddMinutes(5), Attempts = 0
        });
        _jwt.Setup(j => j.HashToken("123456")).Returns("correct-hash");
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", Status = "suspended" });

        var svc = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.VerifyLoginEmailOtpAsync(challengeId, "123456", null, null));

        // I-1: Challenge must NOT be consumed when user is suspended
        _challenges.Verify(c => c.MarkVerifiedAsync(It.IsAny<Guid>()), Times.Never);
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisableMfa_ShouldCallResetColumns()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.ResetMfaColumnsAsync(userId)).ReturnsAsync(true);
        _backupCodes.Setup(b => b.DeleteAllByUserAsync(userId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await svc.DisableMfaAsync(userId);

        _users.Verify(u => u.ResetMfaColumnsAsync(userId), Times.Once);
        _backupCodes.Verify(b => b.DeleteAllByUserAsync(userId), Times.Once);
    }

    [Fact]
    public async Task DisableMfa_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.ResetMfaColumnsAsync(userId)).ReturnsAsync(false);

        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.DisableMfaAsync(userId));
    }

    [Fact]
    public async Task ResetMfa_ShouldCallResetColumns()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.ResetMfaColumnsAsync(userId)).ReturnsAsync(true);
        _backupCodes.Setup(b => b.DeleteAllByUserAsync(userId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await svc.ResetMfaAsync(userId);

        _users.Verify(u => u.ResetMfaColumnsAsync(userId), Times.Once);
        _backupCodes.Verify(b => b.DeleteAllByUserAsync(userId), Times.Once);
    }

    [Fact]
    public async Task ResetMfa_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.ResetMfaColumnsAsync(userId)).ReturnsAsync(false);

        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.ResetMfaAsync(userId));
    }

    [Fact]
    public async Task SetMfaMethod_ShouldSetMethodAndCleanUp_WhenUserFound()
    {
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _users.Setup(u => u.SetMfaMethodAsync(userId, "email_otp")).ReturnsAsync(true);
        _challenges.Setup(c => c.InvalidateActiveByTypeAsync(userId, "email_otp")).Returns(Task.CompletedTask);
        _backupCodes.Setup(b => b.DeleteAllByUserAsync(userId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await svc.SetMfaMethodAsync(userId, "email_otp", adminId);

        _users.Verify(u => u.SetMfaMethodAsync(userId, "email_otp"), Times.Once);
        _challenges.Verify(c => c.InvalidateActiveByTypeAsync(userId, "email_otp"), Times.Once);
        _backupCodes.Verify(b => b.DeleteAllByUserAsync(userId), Times.Once);
        _auditLog.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.UserId == userId && l.EventType == "mfa_method_set")), Times.Once);
    }

    [Fact]
    public async Task SetMfaMethod_ShouldThrow_WhenUserNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.SetMfaMethodAsync(userId, "email_otp")).ReturnsAsync(false);

        var svc = CreateService();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.SetMfaMethodAsync(userId, "email_otp", Guid.NewGuid()));
    }

    // ── VerifyLoginTotpAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyLoginTotp_ShouldReturnTokens_WhenTotpValid()
    {
        SetupTransaction();
        SetupConfig();

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var secretBytes = new byte[20];
        long matchedStep = 100L;

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", FullName = "Test", CompanyId = Guid.NewGuid(),
                Status = "active", MfaTotpEnrolled = true, MfaTotpSecret = "encrypted", MfaTotpLastUsedStep = null });
        _encryptor.Setup(e => e.Decrypt("encrypted")).Returns(secretBytes);
        _totpVerifier.Setup(t => t.Verify(secretBytes, "123456", out matchedStep)).Returns(true);
        _users.Setup(u => u.UpdateMfaTotpLastUsedStepAsync(userId, matchedStep)).Returns(Task.CompletedTask);

        _sessions.Setup(s => s.EvictOldestIfOverLimitAsync(userId, It.IsAny<int>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((Session s, IDbConnection _, IDbTransaction _) => { s.Id = sessionId; return s; });
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-rt", "hashed-rt"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((RefreshToken t, IDbConnection _, IDbTransaction _) => t);
        _roles.Setup(r => r.GetPlatformRoleNamesForUserAsync(userId)).ReturnsAsync(new List<string>());
        _jwt.Setup(j => j.IssueAccessTokenAsync(It.IsAny<User>(), sessionId, It.IsAny<IEnumerable<string>>())).ReturnsAsync("access-token");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateLastSeenAtAsync(userId, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.VerifyLoginTotpAsync(userId, "123456", "1.2.3.4", "agent");

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("plain-rt", result.RefreshToken);
        _users.Verify(u => u.UpdateMfaTotpLastUsedStepAsync(userId, matchedStep), Times.Once);
        _users.Verify(u => u.UpdateLastSeenAtAsync(userId, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task VerifyLoginTotp_ShouldThrow_WhenNotEnrolled()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpEnrolled = false });

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.VerifyLoginTotpAsync(userId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyLoginTotp_ShouldThrow_WhenUserSuspended()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpEnrolled = true, Status = "suspended" });

        var svc = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.VerifyLoginTotpAsync(userId, "123456", null, null));
    }

    [Fact]
    public async Task VerifyLoginTotp_ShouldThrow_WhenCodeInvalid()
    {
        var userId = Guid.NewGuid();
        var secretBytes = new byte[20];
        long matchedStep = 0L;

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpEnrolled = true, MfaTotpSecret = "encrypted" });
        _encryptor.Setup(e => e.Decrypt("encrypted")).Returns(secretBytes);
        _totpVerifier.Setup(t => t.Verify(secretBytes, "000000", out matchedStep)).Returns(false);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyLoginTotpAsync(userId, "000000", null, null));
    }

    [Fact]
    public async Task VerifyLoginTotp_ShouldThrow_WhenStepReplayed()
    {
        var userId = Guid.NewGuid();
        var secretBytes = new byte[20];
        long matchedStep = 99L;

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpEnrolled = true, MfaTotpSecret = "encrypted",
                MfaTotpLastUsedStep = 99L });
        _encryptor.Setup(e => e.Decrypt("encrypted")).Returns(secretBytes);
        _totpVerifier.Setup(t => t.Verify(secretBytes, "123456", out matchedStep)).Returns(true);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyLoginTotpAsync(userId, "123456", null, null));
    }

    // ── Backup Codes ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBackupCodes_ShouldReturn8Codes_WhenEnrolled()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpEnrolled = true });
        _backupCodes.Setup(b => b.ReplaceAllAsync(userId, It.IsAny<IEnumerable<MfaBackupCode>>())).Returns(Task.CompletedTask);
        _jwt.Setup(j => j.HashToken(It.IsAny<string>())).Returns("hashed");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.GenerateBackupCodesAsync(userId);

        Assert.Equal(8, result.Count);
        Assert.Equal(8, result.Codes.Count());
        Assert.All(result.Codes, code => Assert.Equal(10, code.Length));
        _backupCodes.Verify(b => b.ReplaceAllAsync(userId, It.IsAny<IEnumerable<MfaBackupCode>>()), Times.Once);
    }

    [Fact]
    public async Task GenerateBackupCodes_ShouldThrow_WhenNotEnrolled()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaTotpEnrolled = false });

        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GenerateBackupCodesAsync(userId));
    }

    [Fact]
    public async Task VerifyBackupCode_ShouldReturnTokens_WhenCodeValid()
    {
        SetupTransaction();
        SetupConfig();

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var codeId = Guid.NewGuid();

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", FullName = "Test",
                CompanyId = Guid.NewGuid(), Status = "active", MfaTotpEnrolled = true });
        _jwt.Setup(j => j.HashToken("ABCDE12345")).Returns("code-hash");
        _backupCodes.Setup(b => b.GetUnusedByUserAndHashAsync(userId, "code-hash"))
            .ReturnsAsync(new MfaBackupCode { Id = codeId, UserId = userId });
        _backupCodes.Setup(b => b.MarkUsedAsync(codeId)).Returns(Task.CompletedTask);

        _sessions.Setup(s => s.EvictOldestIfOverLimitAsync(userId, It.IsAny<int>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((Session s, IDbConnection _, IDbTransaction _) => { s.Id = sessionId; return s; });
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-rt", "hashed-rt"));
        _refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .ReturnsAsync((RefreshToken t, IDbConnection _, IDbTransaction _) => t);
        _roles.Setup(r => r.GetPlatformRoleNamesForUserAsync(userId)).ReturnsAsync(new List<string>());
        _jwt.Setup(j => j.IssueAccessTokenAsync(It.IsAny<User>(), sessionId, It.IsAny<IEnumerable<string>>())).ReturnsAsync("access-token");
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateLastSeenAtAsync(userId, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        var result = await svc.VerifyBackupCodeAsync(userId, "ABCDE12345", "1.2.3.4", "agent");

        Assert.Equal("access-token", result.AccessToken);
        _backupCodes.Verify(b => b.MarkUsedAsync(codeId), Times.Once);
    }

    [Fact]
    public async Task VerifyBackupCode_ShouldThrow_WhenCodeInvalid()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Status = "active", MfaTotpEnrolled = true });
        _jwt.Setup(j => j.HashToken("BADCODE000")).Returns("bad-hash");
        _backupCodes.Setup(b => b.GetUnusedByUserAndHashAsync(userId, "bad-hash"))
            .ReturnsAsync((MfaBackupCode?)null);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var svc = CreateService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.VerifyBackupCodeAsync(userId, "BADCODE000", null, null));
    }

    [Fact]
    public async Task VerifyBackupCode_ShouldThrow_WhenUserSuspended()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Status = "suspended", MfaTotpEnrolled = true });

        var svc = CreateService();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.VerifyBackupCodeAsync(userId, "ABCDE12345", null, null));
    }

    // ── GetMfaStatusAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMfaStatus_ShouldReturnStatusWithBackupCodeCount()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaEnabled = true, MfaMethod = "totp", MfaTotpEnrolled = true });
        _backupCodes.Setup(b => b.CountUnusedByUserAsync(userId)).ReturnsAsync(6);

        var svc = CreateService();
        var result = await svc.GetMfaStatusAsync(userId);

        Assert.True(result.MfaEnabled);
        Assert.Equal("totp", result.MfaMethod);
        Assert.True(result.MfaTotpEnrolled);
        Assert.Equal(6, result.BackupCodesRemaining);
    }

    [Fact]
    public async Task GetMfaStatus_ShouldReturnZeroBackupCodes_WhenNotEnrolled()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, MfaEnabled = false, MfaTotpEnrolled = false });

        var svc = CreateService();
        var result = await svc.GetMfaStatusAsync(userId);

        Assert.False(result.MfaEnabled);
        Assert.Equal(0, result.BackupCodesRemaining);
        // Should not query backup codes when not enrolled
        _backupCodes.Verify(b => b.CountUnusedByUserAsync(It.IsAny<Guid>()), Times.Never);
    }
}
