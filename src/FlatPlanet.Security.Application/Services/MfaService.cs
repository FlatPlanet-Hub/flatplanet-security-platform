using System.Security.Cryptography;
using System.Text.Json;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.DTOs.Mfa;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class MfaService : IMfaService
{
    private readonly IMfaChallengeRepository _challenges;
    private readonly IUserRepository _users;
    private readonly ISmsSender _sms;
    private readonly ISecurityConfigRepository _securityConfig;
    private readonly IJwtService _jwt;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IRoleRepository _roles;
    private readonly IDbConnectionFactory _db;
    private readonly IIdentityVerificationService _identityVerification;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MfaService> _logger;

    public MfaService(
        IMfaChallengeRepository challenges,
        IUserRepository users,
        ISmsSender sms,
        ISecurityConfigRepository securityConfig,
        IJwtService jwt,
        IAuditLogRepository auditLog,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IRoleRepository roles,
        IDbConnectionFactory db,
        IIdentityVerificationService identityVerification,
        IMemoryCache cache,
        ILogger<MfaService> logger)
    {
        _challenges = challenges;
        _users = users;
        _sms = sms;
        _securityConfig = securityConfig;
        _jwt = jwt;
        _auditLog = auditLog;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _roles = roles;
        _db = db;
        _identityVerification = identityVerification;
        _cache = cache;
        _logger = logger;
    }

    public async Task<EnrollPhoneResponse> EnrollAndSendOtpAsync(Guid userId, string phoneNumber)
    {
        var (expiryMinutes, otpLength) = await GetOtpConfigAsync();

        // Rate limit: if an active unexpired challenge exists, return it without issuing a new SMS
        var existing = await _challenges.GetActiveByUserIdAsync(userId);
        if (existing is not null)
            return new EnrollPhoneResponse { MaskedPhone = MaskPhone(phoneNumber), ExpiresAt = existing.ExpiresAt };

        var otp = GenerateOtp(otpLength);
        var challenge = await _challenges.CreateAsync(new MfaChallenge
        {
            UserId      = userId,
            PhoneNumber = phoneNumber,
            OtpHash     = _jwt.HashToken(otp),
            ExpiresAt   = DateTime.UtcNow.AddMinutes(expiryMinutes)
        });

        await _users.UpdatePhoneNumberAsync(userId, phoneNumber);

        try
        {
            await _sms.SendAsync(phoneNumber, $"Your FlatPlanet verification code is: {otp}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS send failed for user {UserId}", userId);
            throw new ServiceUnavailableException("SMS service is temporarily unavailable. Please try again.");
        }

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaOtpIssued,
            Details   = JsonSerializer.Serialize(new { masked = MaskPhone(phoneNumber) })
        });

        return new EnrollPhoneResponse { MaskedPhone = MaskPhone(phoneNumber), ExpiresAt = challenge.ExpiresAt };
    }

    public async Task VerifyOtpAsync(Guid userId, string code)
    {
        var maxAttempts = await GetMaxAttemptsAsync();
        var challenge   = await _challenges.GetActiveByUserIdAsync(userId)
            ?? throw new KeyNotFoundException("No active MFA challenge found.");

        if (challenge.ExpiresAt < DateTime.UtcNow)
            throw new ArgumentException("OTP has expired.");

        if (challenge.Attempts >= maxAttempts)
            throw new TooManyRequestsException("Maximum OTP attempts exceeded.");

        if (_jwt.HashToken(code) != challenge.OtpHash)
        {
            await _challenges.IncrementAttemptsAsync(challenge.Id);
            await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaFailed });
            throw new ArgumentException("Invalid OTP.");
        }

        await _challenges.MarkVerifiedAsync(challenge.Id);
        await _users.UpdateMfaEnabledAsync(userId, true);

        await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaVerified });
        await _identityVerification.SyncStatusAsync(userId);
    }

    public async Task<MfaChallenge> SendLoginOtpAsync(Guid userId, string phoneNumber)
    {
        var (expiryMinutes, otpLength) = await GetOtpConfigAsync();

        await _challenges.InvalidateActiveAsync(userId);

        var otp = GenerateOtp(otpLength);
        var challenge = await _challenges.CreateAsync(new MfaChallenge
        {
            UserId      = userId,
            PhoneNumber = phoneNumber,
            OtpHash     = _jwt.HashToken(otp),
            ExpiresAt   = DateTime.UtcNow.AddMinutes(expiryMinutes)
        });

        try
        {
            await _sms.SendAsync(phoneNumber, $"Your FlatPlanet login code is: {otp}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMS send failed for login OTP user {UserId}", userId);
            throw new ServiceUnavailableException("SMS service is temporarily unavailable. Please try again.");
        }

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaOtpIssued,
            Details   = JsonSerializer.Serialize(new { type = "login", masked = MaskPhone(phoneNumber) })
        });

        return challenge;
    }

    public async Task<LoginResponse> VerifyLoginOtpAsync(Guid challengeId, string code, string? ipAddress, string? userAgent)
    {
        var maxAttempts = await GetMaxAttemptsAsync();
        var challenge   = await _challenges.GetByIdAsync(challengeId)
            ?? throw new KeyNotFoundException("MFA challenge not found.");

        if (challenge.ExpiresAt < DateTime.UtcNow)
            throw new ArgumentException("OTP has expired.");

        if (challenge.Attempts >= maxAttempts)
            throw new TooManyRequestsException("Maximum OTP attempts exceeded.");

        if (_jwt.HashToken(code) != challenge.OtpHash)
        {
            await _challenges.IncrementAttemptsAsync(challenge.Id);
            await _auditLog.LogAsync(new AuthAuditLog { UserId = challenge.UserId, EventType = AuditEventType.MfaFailed });
            throw new ArgumentException("Invalid OTP.");
        }

        await _challenges.MarkVerifiedAsync(challenge.Id);

        var user = await _users.GetByIdAsync(challenge.UserId)
            ?? throw new KeyNotFoundException("User not found.");

        var config = (await _securityConfig.GetAllAsync()).ToDictionary(c => c.ConfigKey, c => c.ConfigValue);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var now                = DateTime.UtcNow;
        var absoluteTimeout    = Cfg("session_absolute_timeout_minutes", 480);
        var idleTimeoutMinutes = Cfg("session_idle_timeout_minutes", 30);
        var refreshExpiryDays  = Cfg("jwt_refresh_expiry_days", 7);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);
        var maxSessions        = Cfg("max_concurrent_sessions", 3);

        // Enforce the same concurrent-session cap that AuthService applies on non-MFA login.
        // Without this, MFA users could accumulate unlimited sessions.
        var activeSessions = await _sessions.CountActiveByUserAsync(user.Id);
        if (activeSessions >= maxSessions)
        {
            var oldest = await _sessions.GetOldestActiveByUserAsync(user.Id);
            if (oldest is not null)
                await _sessions.EndSessionAsync(oldest.Id, "replaced");
        }

        Session session;
        string refreshTokenPlain;

        using (var conn = await _db.CreateConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                session = await _sessions.CreateAsync(new Session
                {
                    UserId             = user.Id,
                    IpAddress          = ipAddress,
                    UserAgent          = userAgent,
                    ExpiresAt          = now.AddMinutes(absoluteTimeout),
                    IdleTimeoutMinutes = idleTimeoutMinutes
                }, conn, tx);

                var (plain, hash) = _jwt.GenerateRefreshToken();
                refreshTokenPlain = plain;

                await _refreshTokens.CreateAsync(new RefreshToken
                {
                    UserId    = user.Id,
                    SessionId = session.Id,
                    TokenHash = hash,
                    ExpiresAt = now.AddDays(refreshExpiryDays)
                }, conn, tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = _jwt.IssueAccessToken(user, session.Id, platformRoles);

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = user.Id,
            EventType = AuditEventType.MfaLoginVerified,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details   = JsonSerializer.Serialize(new { session_id = session.Id })
        });

        return new LoginResponse
        {
            AccessToken  = accessToken,
            RefreshToken = refreshTokenPlain,
            ExpiresIn    = accessExpiryMinutes * 60,
            User         = new UserProfileDto
            {
                UserId    = user.Id,
                Email     = user.Email,
                FullName  = user.FullName,
                CompanyId = user.CompanyId.ToString()
            }
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(int expiryMinutes, int otpLength)> GetOtpConfigAsync()
    {
        return await _cache.GetOrCreateAsync("mfa_otp_config", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var raw = (await _securityConfig.GetAllAsync()).ToDictionary(c => c.ConfigKey, c => c.ConfigValue);
            int Cfg(string key, int def) =>
                raw.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;
            return (Cfg("mfa_otp_expiry_minutes", 10), Cfg("mfa_otp_length", 6));
        });
    }

    private async Task<int> GetMaxAttemptsAsync()
    {
        return await _cache.GetOrCreateAsync("mfa_otp_max_attempts", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var raw = await _securityConfig.GetValueAsync("mfa_otp_max_attempts");
            return int.TryParse(raw, out var n) ? n : 3;
        });
    }

    private static string GenerateOtp(int length) =>
        string.Join("", RandomNumberGenerator.GetBytes(length).Select(b => (b % 10).ToString()));

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 7) return "*****";
        return phone[..3] + "*****" + phone[^4..];
    }
}
