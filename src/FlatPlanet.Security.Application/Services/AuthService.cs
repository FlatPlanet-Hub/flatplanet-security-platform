using System.Text.Json;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;

namespace FlatPlanet.Security.Application.Services;

public class AuthService : IAuthService
{
    private readonly ISupabaseAuthClient _supabaseAuth;
    private readonly IJwtService _jwt;
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILoginAttemptRepository _loginAttempts;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISecurityConfigRepository _securityConfig;
    private readonly IRoleRepository _roles;
    private readonly IDbConnectionFactory _db;

    public AuthService(
        ISupabaseAuthClient supabaseAuth,
        IJwtService jwt,
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        ILoginAttemptRepository loginAttempts,
        IAuditLogRepository auditLog,
        ISecurityConfigRepository securityConfig,
        IRoleRepository roles,
        IDbConnectionFactory db)
    {
        _supabaseAuth = supabaseAuth;
        _jwt = jwt;
        _users = users;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _loginAttempts = loginAttempts;
        _auditLog = auditLog;
        _securityConfig = securityConfig;
        _roles = roles;
        _db = db;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        var now = DateTime.UtcNow;

        // Fix 7: load all config in one call
        var allConfig = (await _securityConfig.GetAllAsync())
            .ToDictionary(c => c.ConfigKey, c => c.ConfigValue);
        int Cfg(string key, int def) =>
            allConfig.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        // 1. Rate limit check (Fix 11: count only failures from this IP)
        if (!string.IsNullOrEmpty(ipAddress))
        {
            var rateLimitPerIp = Cfg("rate_limit_login_per_ip_per_minute", 5);
            var ipFailures = await _loginAttempts.CountRecentFailuresByIpAsync(ipAddress, now.AddMinutes(-1));
            if (ipFailures >= rateLimitPerIp)
                throw new TooManyRequestsException("Too many login attempts from this IP.");
        }

        // 2. Account lockout check
        var maxFailures = Cfg("max_failed_login_attempts", 5);
        var lockoutMinutes = Cfg("lockout_duration_minutes", 30);
        var recentFailures = await _loginAttempts.CountRecentFailuresByEmailAsync(
            request.Email, now.AddMinutes(-lockoutMinutes));

        if (recentFailures >= maxFailures)
            throw new AccountLockedException("Account is temporarily locked. Please try again later.");

        // 3. Verify with Supabase Auth (Fix 12: throws on infra errors)
        var authResult = await _supabaseAuth.SignInAsync(request.Email, request.Password);

        if (authResult is null)
        {
            await _loginAttempts.RecordAsync(new LoginAttempt
            {
                Email = request.Email,
                IpAddress = ipAddress,
                Success = false,
                AttemptedAt = now
            });
            // Fix 4: use JsonSerializer — no injection risk
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = null,
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details = JsonSerializer.Serialize(new { email = request.Email })
            });
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        // 4. Lookup user
        var user = await _users.GetByIdAsync(authResult.UserId)
            ?? throw new UnauthorizedAccessException("User not found.");

        // 5. Check user status
        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        // 6. Session limit check
        var maxSessions = Cfg("max_concurrent_sessions", 3);
        var activeSessions = await _sessions.CountActiveByUserAsync(user.Id);
        if (activeSessions >= maxSessions)
        {
            var oldest = await _sessions.GetOldestActiveByUserAsync(user.Id);
            if (oldest is not null)
                await _sessions.EndSessionAsync(oldest.Id, "replaced");
        }

        // Fix 5: wrap session + refresh token creation in a transaction
        var absoluteTimeout = Cfg("session_absolute_timeout_minutes", 480);
        var refreshExpiryDays = Cfg("jwt_refresh_expiry_days", 7);

        Session session;
        string refreshTokenPlain;

        using (var conn = await _db.CreateConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                session = await _sessions.CreateAsync(new Session
                {
                    UserId = user.Id,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    ExpiresAt = now.AddMinutes(absoluteTimeout)
                }, conn, tx);

                var (plain, hash) = _jwt.GenerateRefreshToken();
                refreshTokenPlain = plain;

                await _refreshTokens.CreateAsync(new RefreshToken
                {
                    UserId = user.Id,
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

        // Fix 1: include session_id + platform roles in JWT
        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken = _jwt.IssueAccessToken(user, session.Id, platformRoles);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = user.Id,
            EventType = AuditEventType.LoginSuccess,
            IpAddress = ipAddress,
            UserAgent = userAgent
        });
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = user.Id,
            EventType = AuditEventType.SessionStart,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details = JsonSerializer.Serialize(new { session_id = session.Id })
        });
        await _users.UpdateLastSeenAtAsync(user.Id, now);
        await _loginAttempts.RecordAsync(new LoginAttempt
        {
            Email = request.Email,
            IpAddress = ipAddress,
            Success = true,
            AttemptedAt = now
        });

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenPlain,
            ExpiresIn = accessExpiryMinutes * 60,
            User = new UserProfileDto
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                CompanyId = user.CompanyId.ToString()
            }
        };
    }

    public async Task LogoutAsync(Guid sessionId, Guid userId, string? ipAddress)
    {
        await _sessions.EndSessionAsync(sessionId, "logout");
        await _refreshTokens.RevokeAllByUserAsync(userId, "logout");
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = userId,
            EventType = AuditEventType.Logout,
            IpAddress = ipAddress,
            Details = JsonSerializer.Serialize(new { session_id = sessionId })
        });
    }

    public async Task<RefreshResponse> RefreshAsync(RefreshRequest request, string? ipAddress)
    {
        var tokenHash = _jwt.HashToken(request.RefreshToken);
        var stored = await _refreshTokens.GetByTokenHashAsync(tokenHash);

        if (stored is null || stored.Revoked || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var user = await _users.GetByIdAsync(stored.UserId)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        var refreshExpiryDays = await _securityConfig.GetIntValueAsync("jwt_refresh_expiry_days", 7);

        await _refreshTokens.RevokeAsync(stored.Id, "rotated");

        var (newTokenPlain, newTokenHash) = _jwt.GenerateRefreshToken();
        await _refreshTokens.CreateAsync(new RefreshToken
        {
            UserId = user.Id,
            SessionId = stored.SessionId,
            TokenHash = newTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays)
        });

        if (stored.SessionId.HasValue)
            await _sessions.UpdateLastActiveAtAsync(stored.SessionId.Value, DateTime.UtcNow);

        // Fix 1: include session_id + platform roles in refreshed token
        var sessionId = stored.SessionId ?? Guid.Empty;
        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken = _jwt.IssueAccessToken(user, sessionId, platformRoles);
        var accessExpiryMinutes = await _securityConfig.GetIntValueAsync("jwt_access_expiry_minutes", 60);

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = user.Id,
            EventType = AuditEventType.TokenRefresh,
            IpAddress = ipAddress
        });

        return new RefreshResponse
        {
            AccessToken = accessToken,
            RefreshToken = newTokenPlain,
            ExpiresIn = accessExpiryMinutes * 60
        };
    }

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        return new UserProfileResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            RoleTitle = user.RoleTitle,
            CompanyId = user.CompanyId.ToString(),
            Status = user.Status,
            LastSeenAt = user.LastSeenAt
        };
    }
}
