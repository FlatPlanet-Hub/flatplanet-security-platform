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
using OtpNet;

namespace FlatPlanet.Security.Application.Services;

public class MfaService : IMfaService
{
    private readonly IMfaChallengeRepository _challenges;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly ISecurityConfigRepository _securityConfig;
    private readonly IJwtService _jwt;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IRoleRepository _roles;
    private readonly IDbConnectionFactory _db;
    private readonly IIdentityVerificationService _identityVerification;
    private readonly ITotpSecretEncryptor _encryptor;
    private readonly ITotpVerifier _totpVerifier;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MfaService> _logger;

    public MfaService(
        IMfaChallengeRepository challenges,
        IUserRepository users,
        IEmailService email,
        ISecurityConfigRepository securityConfig,
        IJwtService jwt,
        IAuditLogRepository auditLog,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IRoleRepository roles,
        IDbConnectionFactory db,
        IIdentityVerificationService identityVerification,
        ITotpSecretEncryptor encryptor,
        ITotpVerifier totpVerifier,
        IMemoryCache cache,
        ILogger<MfaService> logger)
    {
        _challenges = challenges;
        _users = users;
        _email = email;
        _securityConfig = securityConfig;
        _jwt = jwt;
        _auditLog = auditLog;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _roles = roles;
        _db = db;
        _identityVerification = identityVerification;
        _encryptor = encryptor;
        _totpVerifier = totpVerifier;
        _cache = cache;
        _logger = logger;
    }

    // ── TOTP Enrolment ───────────────────────────────────────────────────────

    public async Task<BeginTotpEnrolmentResponse> BeginTotpEnrolmentAsync(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.MfaTotpEnrolled)
            throw new InvalidOperationException("TOTP is already enrolled for this account.");

        var secret = KeyGeneration.GenerateRandomKey(20);
        var encrypted = _encryptor.Encrypt(secret);
        await _users.UpdateMfaTotpSecretAsync(userId, encrypted);

        var issuer = await GetTotpIssuerAsync();
        var base32Secret = Base32Encoding.ToString(secret);
        var qrCodeUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

        return new BeginTotpEnrolmentResponse { QrCodeUri = qrCodeUri };
    }

    public async Task<LoginResponse> VerifyTotpEnrolmentAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (string.IsNullOrEmpty(user.MfaTotpSecret))
            throw new InvalidOperationException("TOTP enrolment has not been started. Call begin-enrol first.");

        var secret = _encryptor.Decrypt(user.MfaTotpSecret);

        // P1-1: Verify code and capture matched step for replay detection
        if (!_totpVerifier.Verify(secret, totpCode, out var matchedStep))
        {
            await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaFailed });
            throw new UnauthorizedAccessException("Invalid TOTP code.");
        }

        // P1-1: Reject if this time step was already used (replay within 90-second window)
        if (user.MfaTotpLastUsedStep.HasValue && matchedStep <= user.MfaTotpLastUsedStep.Value)
        {
            await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaFailed });
            throw new UnauthorizedAccessException("TOTP code has already been used. Wait for the next code.");
        }

        // BR-2: Single atomic UPDATE — sets enrolled=true, mfa_method='totp', and last_used_step together.
        // Splitting these across two writes could leave last_used_step committed but enrolled=false,
        // causing the next valid code to be rejected as a replay.
        await _users.CompleteTotpEnrolmentAsync(userId, matchedStep);
        await _identityVerification.SyncStatusAsync(userId, true);

        var (session, refreshTokenPlain, config) = await CreateSessionInTransactionAsync(user.Id, ipAddress, userAgent);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        // P1-4: Blocking audit log — MFA enrolment events must be guaranteed
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaEnrolmentComplete,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details   = JsonSerializer.Serialize(new { method = "totp", session_id = session.Id })
        });

        return new LoginResponse
        {
            AccessToken        = accessToken,
            RefreshToken       = refreshTokenPlain,
            ExpiresIn          = Cfg("jwt_access_expiry_minutes", 60) * 60,
            IdleTimeoutMinutes = Cfg("session_idle_timeout_minutes", 30),
            User               = new UserProfileDto
            {
                UserId    = user.Id,
                Email     = user.Email,
                FullName  = user.FullName,
                CompanyId = user.CompanyId.ToString()
            }
        };
    }

    // ── TOTP Login ───────────────────────────────────────────────────────────

    public async Task<LoginResponse> VerifyLoginTotpAsync(Guid userId, string totpCode, string? ipAddress, string? userAgent)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        // P1-2: Reject if TOTP enrolment was not completed
        if (!user.MfaTotpEnrolled)
            throw new InvalidOperationException("TOTP is not enrolled for this account.");

        // BR-1: Status check — [AllowAnonymous] means no middleware protection on this endpoint.
        // A user suspended after the MFA gate in LoginAsync must be rejected here.
        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        if (string.IsNullOrEmpty(user.MfaTotpSecret))
            throw new InvalidOperationException("TOTP is not configured for this account.");

        var secret = _encryptor.Decrypt(user.MfaTotpSecret);

        // P1-1: Verify code and capture matched step for replay detection
        if (!_totpVerifier.Verify(secret, totpCode, out var matchedStep))
        {
            await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaFailed, IpAddress = ipAddress });
            throw new UnauthorizedAccessException("Invalid TOTP code.");
        }

        // P1-1: Reject replay within the 90-second verification window
        if (user.MfaTotpLastUsedStep.HasValue && matchedStep <= user.MfaTotpLastUsedStep.Value)
        {
            await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaFailed, IpAddress = ipAddress });
            throw new UnauthorizedAccessException("TOTP code has already been used. Wait for the next code.");
        }

        await _users.UpdateMfaTotpLastUsedStepAsync(userId, matchedStep);

        var (session, refreshTokenPlain, config) = await CreateSessionInTransactionAsync(user.Id, ipAddress, userAgent);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        // P1-4: Blocking audit log
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaLoginVerified,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details   = JsonSerializer.Serialize(new { method = "totp", session_id = session.Id })
        });

        return new LoginResponse
        {
            AccessToken        = accessToken,
            RefreshToken       = refreshTokenPlain,
            ExpiresIn          = Cfg("jwt_access_expiry_minutes", 60) * 60,
            IdleTimeoutMinutes = Cfg("session_idle_timeout_minutes", 30),
            User               = new UserProfileDto
            {
                UserId    = user.Id,
                Email     = user.Email,
                FullName  = user.FullName,
                CompanyId = user.CompanyId.ToString()
            }
        };
    }

    // ── Email OTP Login ──────────────────────────────────────────────────────

    public async Task<MfaChallenge> SendEmailOtpAsync(Guid userId, string? ipAddress)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var (expiryMinutes, otpLength) = await GetEmailOtpConfigAsync();

        await _challenges.InvalidateActiveByTypeAsync(userId, "email_otp");

        var otp = GenerateOtp(otpLength);
        var challenge = await _challenges.CreateAsync(new MfaChallenge
        {
            UserId        = userId,
            ChallengeType = "email_otp",
            Email         = user.Email,
            OtpHash       = _jwt.HashToken(otp),
            ExpiresAt     = DateTime.UtcNow.AddMinutes(expiryMinutes)
        });

        try
        {
            await _email.SendMfaOtpEmailAsync(user.Email, otp, expiryMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email OTP send failed for user {UserId}", userId);
            throw new ServiceUnavailableException("Email service is temporarily unavailable. Please try again.");
        }

        // P1-4: Blocking audit log
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaOtpIssued,
            IpAddress = ipAddress,
            Details   = JsonSerializer.Serialize(new { type = "email_otp" })
        });

        return challenge;
    }

    public async Task<LoginResponse> VerifyLoginEmailOtpAsync(Guid challengeId, string otpCode, string? ipAddress, string? userAgent)
    {
        var challenge = await _challenges.GetByIdAsync(challengeId)
            ?? throw new KeyNotFoundException("MFA challenge not found.");

        if (challenge.ChallengeType != "email_otp")
            throw new ArgumentException("Invalid challenge type.");

        if (challenge.VerifiedAt is not null)
            throw new InvalidOperationException("Challenge has already been used.");

        if (challenge.ExpiresAt <= DateTime.UtcNow)
            throw new ArgumentException("OTP has expired.");

        var maxAttempts = await GetMaxAttemptsAsync();
        if (challenge.Attempts >= maxAttempts)
            throw new TooManyRequestsException("Maximum OTP attempts exceeded.");

        if (_jwt.HashToken(otpCode) != challenge.OtpHash)
        {
            await _challenges.IncrementAttemptsAsync(challenge.Id);
            // P1-4: Blocking audit log
            await _auditLog.LogAsync(new AuthAuditLog { UserId = challenge.UserId, EventType = AuditEventType.MfaFailed, IpAddress = ipAddress });
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        await _challenges.MarkVerifiedAsync(challenge.Id);

        var user = await _users.GetByIdAsync(challenge.UserId)
            ?? throw new KeyNotFoundException("User not found.");

        // P1-3: Reject if user was suspended after the challenge was issued
        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        var (session, refreshTokenPlain, config) = await CreateSessionInTransactionAsync(user.Id, ipAddress, userAgent);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        // ISO 27001: email OTP does NOT set mfa_verified — only TOTP enrolment does
        // P1-4: Blocking audit log
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = user.Id,
            EventType = AuditEventType.MfaLoginVerified,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details   = JsonSerializer.Serialize(new { method = "email_otp", session_id = session.Id })
        });

        return new LoginResponse
        {
            AccessToken        = accessToken,
            RefreshToken       = refreshTokenPlain,
            ExpiresIn          = Cfg("jwt_access_expiry_minutes", 60) * 60,
            IdleTimeoutMinutes = Cfg("session_idle_timeout_minutes", 30),
            User               = new UserProfileDto
            {
                UserId    = user.Id,
                Email     = user.Email,
                FullName  = user.FullName,
                CompanyId = user.CompanyId.ToString()
            }
        };
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    public async Task DisableMfaAsync(Guid userId)
    {
        // P1-5: Verify user exists — ResetMfaColumnsAsync returns false if not found
        var found = await _users.ResetMfaColumnsAsync(userId);
        if (!found)
            throw new KeyNotFoundException("User not found.");
        await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaDisabled });
    }

    public async Task ResetMfaAsync(Guid userId)
    {
        // P1-5: Verify user exists — ResetMfaColumnsAsync returns false if not found
        var found = await _users.ResetMfaColumnsAsync(userId);
        if (!found)
            throw new KeyNotFoundException("User not found.");
        await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaReset });
    }

    // ── Shared session factory ───────────────────────────────────────────────

    private async Task<(Session session, string refreshTokenPlain, Dictionary<string, string> config)> CreateSessionInTransactionAsync(
        Guid userId, string? ipAddress, string? userAgent)
    {
        // P2-1: Use 5-min cache consistent with AuthService.LoadConfigAsync pattern
        var config = await LoadConfigAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var now              = DateTime.UtcNow;
        var maxSessions      = Cfg("max_concurrent_sessions", 3);
        var absoluteTimeout  = Cfg("session_absolute_timeout_minutes", 480);
        var idleTimeout      = Cfg("session_idle_timeout_minutes", 30);
        var refreshExpiryDays = Cfg("jwt_refresh_expiry_days", 7);

        Session session;
        string refreshTokenPlain;

        using (var conn = await _db.CreateConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                await _sessions.EvictOldestIfOverLimitAsync(userId, maxSessions, conn, tx);

                session = await _sessions.CreateAsync(new Session
                {
                    UserId             = userId,
                    IpAddress          = ipAddress,
                    UserAgent          = userAgent,
                    ExpiresAt          = now.AddMinutes(absoluteTimeout),
                    IdleTimeoutMinutes = idleTimeout
                }, conn, tx);

                var (plain, hash) = _jwt.GenerateRefreshToken();
                refreshTokenPlain = plain;

                await _refreshTokens.CreateAsync(new RefreshToken
                {
                    UserId    = userId,
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

        return (session, refreshTokenPlain, config);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, string>> LoadConfigAsync()
    {
        const string cacheKey = "fp:sec:cfg:all";
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? cached) && cached is not null)
            return cached;

        var configs = await _securityConfig.GetAllAsync();
        var dict = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue);

        _cache.Set(cacheKey, dict, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        return dict;
    }

    private async Task<(int expiryMinutes, int otpLength)> GetEmailOtpConfigAsync()
    {
        // BR-3: Delegate to the shared config cache instead of doing a separate GetAllAsync.
        var config = await LoadConfigAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;
        return (Cfg("mfa_email_otp_expiry_minutes", 10), Cfg("mfa_otp_length", 6));
    }

    private async Task<int> GetMaxAttemptsAsync()
    {
        return await _cache.GetOrCreateAsync("mfa_max_otp_attempts", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var raw = await _securityConfig.GetValueAsync("mfa_max_otp_attempts");
            return int.TryParse(raw, out var n) ? n : 5;
        });
    }

    private async Task<string> GetTotpIssuerAsync()
    {
        return await _cache.GetOrCreateAsync("mfa_totp_issuer", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _securityConfig.GetValueAsync("mfa_totp_issuer") ?? "FlatPlanet";
        }) ?? "FlatPlanet";
    }

    private static string GenerateOtp(int length)
    {
        // N-1: Rejection sampling — skip bytes 250-255 to eliminate modulo bias (256 % 10 != 0).
        var digits = new char[length];
        var filled = 0;
        while (filled < length)
        {
            foreach (var b in RandomNumberGenerator.GetBytes((length - filled) * 2))
            {
                if (filled >= length) break;
                if (b < 250)
                    digits[filled++] = (char)('0' + b % 10);
            }
        }
        return new string(digits);
    }
}
