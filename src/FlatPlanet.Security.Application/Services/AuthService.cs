using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
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

    public AuthService(
        ISupabaseAuthClient supabaseAuth,
        IJwtService jwt,
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        ILoginAttemptRepository loginAttempts,
        IAuditLogRepository auditLog,
        ISecurityConfigRepository securityConfig)
    {
        _supabaseAuth = supabaseAuth;
        _jwt = jwt;
        _users = users;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _loginAttempts = loginAttempts;
        _auditLog = auditLog;
        _securityConfig = securityConfig;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        var now = DateTime.UtcNow;

        // 1. Rate limit check — IP
        if (!string.IsNullOrEmpty(ipAddress))
        {
            var rateLimitPerIp = await _securityConfig.GetIntValueAsync("rate_limit_login_per_ip_per_minute", 5);
            var ipAttempts = await _loginAttempts.CountRecentByIpAsync(ipAddress, now.AddMinutes(-1));
            if (ipAttempts >= rateLimitPerIp)
                throw new TooManyRequestsException("Too many login attempts from this IP.");
        }

        // 2. Account lockout check
        var maxFailures = await _securityConfig.GetIntValueAsync("max_failed_login_attempts", 5);
        var lockoutMinutes = await _securityConfig.GetIntValueAsync("lockout_duration_minutes", 30);
        var recentFailures = await _loginAttempts.CountRecentFailuresByEmailAsync(
            request.Email, now.AddMinutes(-lockoutMinutes));

        if (recentFailures >= maxFailures)
            throw new AccountLockedException("Account is temporarily locked. Please try again later.");

        // 3. Verify with Supabase Auth
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
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = null,
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details = $"{{\"email\":\"{request.Email}\"}}"
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
        var maxSessions = await _securityConfig.GetIntValueAsync("max_concurrent_sessions", 3);
        var activeSessions = await _sessions.CountActiveByUserAsync(user.Id);

        if (activeSessions >= maxSessions)
        {
            var oldest = await _sessions.GetOldestActiveByUserAsync(user.Id);
            if (oldest is not null)
                await _sessions.EndSessionAsync(oldest.Id, "replaced");
        }

        // 7. Create session
        var idleTimeout = await _securityConfig.GetIntValueAsync("session_idle_timeout_minutes", 30);
        var absoluteTimeout = await _securityConfig.GetIntValueAsync("session_absolute_timeout_minutes", 480);

        var session = await _sessions.CreateAsync(new Session
        {
            UserId = user.Id,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = now.AddMinutes(absoluteTimeout)
        });

        // 8. Issue JWT + refresh token
        var accessToken = _jwt.IssueAccessToken(user);
        var (refreshTokenPlain, refreshTokenHash) = _jwt.GenerateRefreshToken();
        var refreshExpiryDays = await _securityConfig.GetIntValueAsync("jwt_refresh_expiry_days", 7);

        await _refreshTokens.CreateAsync(new RefreshToken
        {
            UserId = user.Id,
            SessionId = session.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = now.AddDays(refreshExpiryDays)
        });

        // 9. Audit log
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
            Details = $"{{\"session_id\":\"{session.Id}\"}}"
        });

        // 10. Update last_seen_at
        await _users.UpdateLastSeenAtAsync(user.Id, now);

        // 11. Record success
        await _loginAttempts.RecordAsync(new LoginAttempt
        {
            Email = request.Email,
            IpAddress = ipAddress,
            Success = true,
            AttemptedAt = now
        });

        var accessExpiryMinutes = await _securityConfig.GetIntValueAsync("jwt_access_expiry_minutes", 60);

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
            Details = $"{{\"session_id\":\"{sessionId}\"}}"
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

        // Rotate token
        await _refreshTokens.RevokeAsync(stored.Id, "rotated");

        var (newTokenPlain, newTokenHash) = _jwt.GenerateRefreshToken();
        var refreshExpiryDays = await _securityConfig.GetIntValueAsync("jwt_refresh_expiry_days", 7);

        await _refreshTokens.CreateAsync(new RefreshToken
        {
            UserId = user.Id,
            SessionId = stored.SessionId,
            TokenHash = newTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshExpiryDays)
        });

        if (stored.SessionId.HasValue)
            await _sessions.UpdateLastActiveAtAsync(stored.SessionId.Value, DateTime.UtcNow);

        var accessToken = _jwt.IssueAccessToken(user);
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
