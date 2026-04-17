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
    private readonly IMfaBackupCodeRepository _backupCodes;
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
        IMfaBackupCodeRepository backupCodes,
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
        _backupCodes = backupCodes;
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
        await _users.CompleteTotpEnrolmentAsync(userId, matchedStep);
        await _identityVerification.SyncStatusAsync(userId, true);

        var (session, refreshTokenPlain, config) = await CreateSessionInTransactionAsync(user.Id, ipAddress, userAgent);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        await Task.WhenAll(
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.MfaEnrolmentComplete,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { method = "totp", session_id = session.Id })
            }),
            _users.UpdateLastSeenAtAsync(user.Id, DateTime.UtcNow)
        );

        return new LoginResponse
        {
            MfaEnrolled        = true,
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

        // GAP-G3: Update last_seen_at alongside the audit log
        await Task.WhenAll(
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.MfaLoginVerified,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { method = "totp", session_id = session.Id })
            }),
            _users.UpdateLastSeenAtAsync(user.Id, DateTime.UtcNow)
        );

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

        // GAP-G2: Reject suspended/inactive users — this method can be called from multiple paths.
        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

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

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaOtpIssued,
            IpAddress = ipAddress,
            Details   = JsonSerializer.Serialize(new { type = "email_otp" })
        });

        return challenge;
    }

    public async Task<MfaChallenge> ResendEmailOtpAsync(Guid userId, string? ipAddress)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!user.MfaEnabled || user.MfaMethod != "email_otp")
            throw new InvalidOperationException("Email OTP is not enabled for this account.");

        // Delegates to SendEmailOtpAsync which invalidates the old challenge and sends a fresh one.
        return await SendEmailOtpAsync(userId, ipAddress);
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
            await _auditLog.LogAsync(new AuthAuditLog { UserId = challenge.UserId, EventType = AuditEventType.MfaFailed, IpAddress = ipAddress });
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        // I-1: Load user and check status BEFORE consuming the challenge.
        // Consuming first (old order) wasted the challenge on a suspended user with no way to recover it.
        var user = await _users.GetByIdAsync(challenge.UserId)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        await _challenges.MarkVerifiedAsync(challenge.Id);

        var (session, refreshTokenPlain, config) = await CreateSessionInTransactionAsync(user.Id, ipAddress, userAgent);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        // GAP-G3: Update last_seen_at alongside the audit log
        await Task.WhenAll(
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.MfaLoginVerified,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { method = "email_otp", session_id = session.Id })
            }),
            _users.UpdateLastSeenAtAsync(user.Id, DateTime.UtcNow)
        );

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

    // ── Backup Codes ─────────────────────────────────────────────────────────

    public async Task<GenerateBackupCodesResponse> GenerateBackupCodesAsync(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!user.MfaTotpEnrolled)
            throw new InvalidOperationException("Backup codes require TOTP to be enrolled first.");

        var plainCodes = Enumerable.Range(0, 8).Select(_ => GenerateBackupCode()).ToList();

        // P2-2: Atomic replace — delete old codes and insert new ones in a single transaction.
        await _backupCodes.ReplaceAllAsync(userId, plainCodes.Select(code => new MfaBackupCode
        {
            UserId   = userId,
            CodeHash = _jwt.HashToken(code)
        }));

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.MfaBackupCodesGenerated,
            Details   = JsonSerializer.Serialize(new { count = plainCodes.Count })
        });

        return new GenerateBackupCodesResponse { Codes = plainCodes, Count = plainCodes.Count };
    }

    public async Task<LoginResponse> VerifyBackupCodeAsync(Guid userId, string backupCode, string? ipAddress, string? userAgent)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!user.MfaTotpEnrolled)
            throw new InvalidOperationException("Backup codes are only available for TOTP-enrolled accounts.");

        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        var codeHash = _jwt.HashToken(backupCode.ToUpperInvariant());
        var stored = await _backupCodes.GetUnusedByUserAndHashAsync(userId, codeHash);

        if (stored is null)
        {
            await _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaFailed, IpAddress = ipAddress });
            throw new UnauthorizedAccessException("Invalid or already-used backup code.");
        }

        await _backupCodes.MarkUsedAsync(stored.Id);

        var (session, refreshTokenPlain, config) = await CreateSessionInTransactionAsync(user.Id, ipAddress, userAgent);
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken   = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        await Task.WhenAll(
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.MfaLoginVerified,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { method = "backup_code", session_id = session.Id })
            }),
            _users.UpdateLastSeenAtAsync(user.Id, DateTime.UtcNow)
        );

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

    // ── Status ───────────────────────────────────────────────────────────────

    public async Task<UserMfaStatusResponse> GetMfaStatusAsync(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var backupCodesRemaining = user.MfaTotpEnrolled
            ? await _backupCodes.CountUnusedByUserAsync(userId)
            : 0;

        return new UserMfaStatusResponse
        {
            MfaEnabled           = user.MfaEnabled,
            MfaMethod            = user.MfaMethod,
            MfaTotpEnrolled      = user.MfaTotpEnrolled,
            BackupCodesRemaining = backupCodesRemaining
        };
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    public async Task SetMfaMethodAsync(Guid userId, string method, Guid performedByUserId)
    {
        var found = await _users.SetMfaMethodAsync(userId, method);
        if (!found)
            throw new KeyNotFoundException("User not found.");

        // Invalidate in-flight email_otp challenges — the only challenge type in the system.
        // TOTP has no challenge record (it is stateless), so no second invalidation is needed.
        await _challenges.InvalidateActiveByTypeAsync(userId, "email_otp");

        await Task.WhenAll(
            _backupCodes.DeleteAllByUserAsync(userId),
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.MfaMethodSet,
                Details   = JsonSerializer.Serialize(new { method, performed_by = performedByUserId })
            }));
    }

    public async Task DisableMfaAsync(Guid userId)
    {
        var found = await _users.ResetMfaColumnsAsync(userId);
        if (!found)
            throw new KeyNotFoundException("User not found.");
        await Task.WhenAll(
            _backupCodes.DeleteAllByUserAsync(userId),
            _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaDisabled }));
    }

    public async Task ResetMfaAsync(Guid userId)
    {
        var found = await _users.ResetMfaColumnsAsync(userId);
        if (!found)
            throw new KeyNotFoundException("User not found.");
        await Task.WhenAll(
            _backupCodes.DeleteAllByUserAsync(userId),
            _auditLog.LogAsync(new AuthAuditLog { UserId = userId, EventType = AuditEventType.MfaReset }));
    }

    // ── Shared session factory ───────────────────────────────────────────────

    private async Task<(Session session, string refreshTokenPlain, Dictionary<string, string> config)> CreateSessionInTransactionAsync(
        Guid userId, string? ipAddress, string? userAgent)
    {
        var config = await LoadConfigAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var now               = DateTime.UtcNow;
        var maxSessions       = Cfg("max_concurrent_sessions", 3);
        var absoluteTimeout   = Cfg("session_absolute_timeout_minutes", 480);
        var idleTimeout       = Cfg("session_idle_timeout_minutes", 30);
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
        var config = await LoadConfigAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;
        return (Cfg("mfa_email_otp_expiry_minutes", 10), Cfg("mfa_otp_length", 6));
    }

    private async Task<int> GetMaxAttemptsAsync()
    {
        // I-2: Use shared config cache instead of a per-key lookup with a separate cache entry.
        var config = await LoadConfigAsync();
        return config.TryGetValue("mfa_max_otp_attempts", out var v) && int.TryParse(v, out var n) ? n : 5;
    }

    private async Task<string> GetTotpIssuerAsync()
    {
        // I-2: Use shared config cache instead of a per-key lookup with a separate cache entry.
        var config = await LoadConfigAsync();
        return config.TryGetValue("mfa_totp_issuer", out var v) ? v : "FlatPlanet";
    }

    private static string GenerateOtp(int length)
    {
        // Rejection sampling to eliminate modulo bias (256 % 10 != 0).
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

    private static string GenerateBackupCode()
    {
        // 32-char unambiguous alphabet (no I, O, 0, 1). 32 divides 256 evenly — no modulo bias.
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(RandomNumberGenerator.GetBytes(10).Select(b => chars[b % 32]).ToArray());
    }
}
