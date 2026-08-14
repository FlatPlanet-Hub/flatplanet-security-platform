using System.Text.Json;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Constants;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class LoginService : ILoginService
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwt;
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILoginAttemptRepository _loginAttempts;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISecurityConfigService _configService;
    private readonly IRoleRepository _roles;
    private readonly IDbConnectionFactory _db;
    private readonly ICompanyRepository _companies;
    private readonly IMfaService _mfa;
    private readonly ISessionPolicyResolver _sessionPolicy;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        IPasswordHasher passwordHasher,
        IJwtService jwt,
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        ILoginAttemptRepository loginAttempts,
        IAuditLogRepository auditLog,
        ISecurityConfigService configService,
        IRoleRepository roles,
        IDbConnectionFactory db,
        ICompanyRepository companies,
        IMfaService mfa,
        ISessionPolicyResolver sessionPolicy,
        IMemoryCache cache,
        ILogger<LoginService> logger)
    {
        _passwordHasher = passwordHasher;
        _jwt            = jwt;
        _users          = users;
        _sessions       = sessions;
        _refreshTokens  = refreshTokens;
        _loginAttempts  = loginAttempts;
        _auditLog       = auditLog;
        _configService  = configService;
        _roles          = roles;
        _db             = db;
        _companies      = companies;
        _mfa            = mfa;
        _sessionPolicy  = sessionPolicy;
        _cache          = cache;
        _logger         = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        var now = DateTime.UtcNow;

        var config = await _configService.GetAllCachedAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var rateLimitPerIp    = Cfg("rate_limit_login_per_ip_per_minute", 5);
        var rateLimitPerEmail = Cfg("rate_limit_login_per_email_per_minute", 10);
        var maxFailures       = Cfg("max_failed_login_attempts", 5);
        var lockoutMinutes    = Cfg("lockout_duration_minutes", 30);

        // Single query replaces 3 parallel COUNT queries — 1 connection checkout instead of 3.
        var checks = await _loginAttempts.GetLoginChecksAsync(
            email:        request.Email,
            ipAddress:    ipAddress,
            ipSince:      now.AddMinutes(-1),
            emailSince:   now.AddMinutes(-1),
            lockoutSince: now.AddMinutes(-lockoutMinutes));

        if (!string.IsNullOrEmpty(ipAddress) && checks.IpFailures >= rateLimitPerIp)
            throw new TooManyRequestsException("Too many login attempts from this IP.");

        if (checks.EmailAttempts >= rateLimitPerEmail)
            throw new TooManyRequestsException("Too many login attempts for this account.");

        if (checks.LockoutFailures >= maxFailures)
            throw new AccountLockedException("Account is temporarily locked. Please try again later.");

        var user = await _users.GetByEmailAsync(request.Email);

        if (user == null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await Task.WhenAll(
                _loginAttempts.RecordAsync(new LoginAttempt
                {
                    Email       = request.Email,
                    IpAddress   = ipAddress,
                    Success     = false,
                    AttemptedAt = now
                }),
                _auditLog.LogAsync(new AuthAuditLog
                {
                    UserId    = null,
                    EventType = AuditEventType.LoginFailure,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Details   = JsonSerializer.Serialize(new { email = request.Email })
                })
            );
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (user.Status != EntityStatus.Active)
            throw new ForbiddenException($"User account is {user.Status}.");

        // Fetch company and platform roles in parallel — both only need user.CompanyId / user.Id.
        var companyTask = _companies.GetByIdAsync(user.CompanyId);
        var rolesTask   = _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        await Task.WhenAll(companyTask, rolesTask);

        var company = companyTask.Result
            ?? throw new UnauthorizedAccessException("Company not found.");
        if (company.Status != EntityStatus.Active)
            throw new ForbiddenException($"Company account is {company.Status}.");

        if (user.MfaEnabled)
        {
            // U1 fix: only gate on TOTP if the method is 'totp' — email_otp users always have MfaTotpEnrolled=false
            // Issue a short-lived enrolment-only token so the user can reach begin-enrol / verify-enrol.
            // No refresh token — this session exists only to complete enrollment (10-min absolute timeout).
            if (user.MfaMethod == "totp" && !user.MfaTotpEnrolled)
            {
                Session enrolSession;
                using (var conn = await _db.CreateConnectionAsync())
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        await _sessions.EvictOldestIfOverLimitAsync(user.Id, Cfg("max_concurrent_sessions", 3), conn, tx);
                        enrolSession = await _sessions.CreateAsync(new Session
                        {
                            UserId             = user.Id,
                            IpAddress          = ipAddress,
                            UserAgent          = userAgent,
                            ExpiresAt          = now.AddMinutes(10),
                            IdleTimeoutMinutes = 10
                        }, conn, tx);
                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }

                var enrolToken = await _jwt.IssueEnrolmentTokenAsync(user, enrolSession.Id);
                return new LoginResponse
                {
                    RequiresMfa         = true,
                    MfaEnrolmentPending = true,
                    MfaMethod           = "totp",
                    AccessToken         = enrolToken,
                    ExpiresIn           = 600,
                    User = new UserProfileDto
                    {
                        UserId    = user.Id,
                        Email     = user.Email,
                        FullName  = user.FullName,
                        CompanyId = user.CompanyId.ToString()
                    }
                };
            }

            if (user.MfaMethod == "totp")
                return new LoginResponse { RequiresMfa = true, MfaMethod = "totp", User = new UserProfileDto { UserId = user.Id } };

            var challenge = await _mfa.SendEmailOtpAsync(user.Id, ipAddress);
            return new LoginResponse { RequiresMfa = true, MfaMethod = "email_otp", ChallengeId = challenge.Id, User = new UserProfileDto { UserId = user.Id } };
        }

        var maxSessions        = Cfg("max_concurrent_sessions", 3);
        var refreshExpiryDays  = Cfg("jwt_refresh_expiry_days", 7);

        // Per-app timeout overrides (apps.session_*_timeout_minutes) win over the
        // platform defaults. Both are stamped onto the session row below.
        var policy             = await _sessionPolicy.ResolveAsync(user.Id, request.AppSlug, config);
        var absoluteTimeout    = policy.AbsoluteTimeoutMinutes;
        var idleTimeoutMinutes = policy.IdleTimeoutMinutes;

        Session session;
        string refreshTokenPlain;

        using (var conn = await _db.CreateConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                await _sessions.EvictOldestIfOverLimitAsync(user.Id, maxSessions, conn, tx);

                session = await _sessions.CreateAsync(new Session
                {
                    UserId             = user.Id,
                    AppId              = policy.AppId,
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

        var platformRoles      = rolesTask.Result;
        var accessToken        = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

        await Task.WhenAll(
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.LoginSuccess,
                IpAddress = ipAddress,
                UserAgent = userAgent
            }),
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.SessionStart,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { session_id = session.Id })
            }),
            _users.UpdateLastSeenAtAsync(user.Id, now),
            _loginAttempts.RecordAsync(new LoginAttempt
            {
                Email       = request.Email,
                IpAddress   = ipAddress,
                Success     = true,
                AttemptedAt = now
            })
        );

        return new LoginResponse
        {
            AccessToken        = accessToken,
            RefreshToken       = refreshTokenPlain,
            ExpiresIn          = accessExpiryMinutes * 60,
            IdleTimeoutMinutes = idleTimeoutMinutes,
            User = new UserProfileDto
            {
                UserId    = user.Id,
                Email     = user.Email,
                FullName  = user.FullName,
                CompanyId = user.CompanyId.ToString()
            }
        };
    }

    public async Task LogoutAsync(Guid? sessionId, Guid userId, string? ipAddress)
    {
        if (sessionId.HasValue)
        {
            await _sessions.EndSessionAsync(sessionId.Value, "logout");
            _cache.Remove($"fp:sec:session:{sessionId.Value}");
        }
        await _refreshTokens.RevokeAllByUserAsync(userId, "logout");
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.Logout,
            IpAddress = ipAddress,
            Details   = JsonSerializer.Serialize(new { session_id = sessionId })
        });
    }

    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress)
    {
        var config = await _configService.GetAllCachedAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var tokenHash = _jwt.HashToken(request.RefreshToken);
        var stored    = await _refreshTokens.GetByTokenHashAsync(tokenHash);

        if (stored is null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Strict rotation: any revoked token is immediately invalid — no grace period
        if (stored.Revoked)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        if (stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var user = await _users.GetByIdAsync(stored.UserId)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.Status != EntityStatus.Active)
            throw new ForbiddenException($"User account is {user.Status}.");

        var refreshExpiryDays = Cfg("jwt_refresh_expiry_days", 7);

        // Generate new token and atomically rotate old → new in a single transaction.
        // Without a transaction, a CreateAsync failure after RotateAsync would leave the
        // old token revoked and no new token existing — locking the user out.
        var (newTokenPlain, newTokenHash) = _jwt.GenerateRefreshToken();

        using (var conn = await _db.CreateConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                await _refreshTokens.RotateAsync(stored.Id, newTokenHash, conn, tx);
                await _refreshTokens.CreateAsync(new RefreshToken
                {
                    UserId    = user.Id,
                    SessionId = stored.SessionId,
                    TokenHash = newTokenHash,
                    ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays)
                }, conn, tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // Reactivate the session — covers the case where idle timeout fired but the
        // refresh token is still valid (e.g. always-on dashboard). Without this,
        // the new access token would be immediately rejected by SessionValidationMiddleware
        // because is_active = false on the dead session row.
        if (stored.SessionId.HasValue)
            await _sessions.ReactivateAsync(stored.SessionId.Value, DateTime.UtcNow);

        var sessionId          = stored.SessionId ?? Guid.Empty;
        var platformRoles      = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken        = await _jwt.IssueAccessTokenAsync(user, sessionId, platformRoles);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = user.Id,
            EventType = AuditEventType.TokenRefresh,
            IpAddress = ipAddress
        });

        return new RefreshResponse
        {
            AccessToken  = accessToken,
            RefreshToken = newTokenPlain,
            ExpiresIn    = accessExpiryMinutes * 60
        };
    }
}
