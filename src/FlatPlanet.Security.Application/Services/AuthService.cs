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
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwt;
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ILoginAttemptRepository _loginAttempts;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISecurityConfigRepository _securityConfig;
    private readonly IRoleRepository _roles;
    private readonly IDbConnectionFactory _db;
    private readonly ICompanyRepository _companies;
    private readonly IUserContextService _userContext;
    private readonly IMfaService _mfa;

    public AuthService(
        IPasswordHasher passwordHasher,
        IJwtService jwt,
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        ILoginAttemptRepository loginAttempts,
        IAuditLogRepository auditLog,
        ISecurityConfigRepository securityConfig,
        IRoleRepository roles,
        IDbConnectionFactory db,
        ICompanyRepository companies,
        IUserContextService userContext,
        IMfaService mfa)
    {
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _users = users;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _loginAttempts = loginAttempts;
        _auditLog = auditLog;
        _securityConfig = securityConfig;
        _roles = roles;
        _db = db;
        _companies = companies;
        _userContext = userContext;
        _mfa = mfa;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent)
    {
        var now = DateTime.UtcNow;

        var config = await LoadConfigAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var rateLimitPerIp    = Cfg("rate_limit_login_per_ip_per_minute", 5);
        var rateLimitPerEmail = Cfg("rate_limit_login_per_email_per_minute", 10);
        var maxFailures       = Cfg("max_failed_login_attempts", 5);
        var lockoutMinutes    = Cfg("lockout_duration_minutes", 30);

        // Run all three rate-limit / lockout checks in parallel
        var ipCheckTask = string.IsNullOrEmpty(ipAddress)
            ? Task.FromResult(0)
            : _loginAttempts.CountRecentFailuresByIpAsync(ipAddress, now.AddMinutes(-1));
        var emailAttemptsTask   = _loginAttempts.CountRecentByEmailAsync(request.Email, now.AddMinutes(-1));
        var recentFailuresTask  = _loginAttempts.CountRecentFailuresByEmailAsync(request.Email, now.AddMinutes(-lockoutMinutes));

        await Task.WhenAll(ipCheckTask, emailAttemptsTask, recentFailuresTask);

        if (!string.IsNullOrEmpty(ipAddress) && ipCheckTask.Result >= rateLimitPerIp)
            throw new TooManyRequestsException("Too many login attempts from this IP.");

        if (emailAttemptsTask.Result >= rateLimitPerEmail)
            throw new TooManyRequestsException("Too many login attempts for this account.");

        if (recentFailuresTask.Result >= maxFailures)
            throw new AccountLockedException("Account is temporarily locked. Please try again later.");

        // Look up user and verify password
        var user = await _users.GetByEmailAsync(request.Email);

        if (user == null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await Task.WhenAll(
                _loginAttempts.RecordAsync(new LoginAttempt
                {
                    Email = request.Email,
                    IpAddress = ipAddress,
                    Success = false,
                    AttemptedAt = now
                }),
                _auditLog.LogAsync(new AuthAuditLog
                {
                    UserId = null,
                    EventType = AuditEventType.LoginFailure,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Details = JsonSerializer.Serialize(new { email = request.Email })
                })
            );
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        // Check user and company status
        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        var company = await _companies.GetByIdAsync(user.CompanyId)
            ?? throw new UnauthorizedAccessException("Company not found.");
        if (company.Status != "active")
            throw new ForbiddenException($"Company account is {company.Status}.");

        // MFA gate — if the user has MFA enabled, issue an OTP and defer token issuance
        if (user.MfaEnabled && !string.IsNullOrEmpty(user.PhoneNumber))
        {
            var challenge = await _mfa.SendLoginOtpAsync(user.Id, user.PhoneNumber);
            return new LoginResponse { RequiresMfa = true, ChallengeId = challenge.Id.ToString() };
        }

        // Session limit check
        var maxSessions = Cfg("max_concurrent_sessions", 3);
        var activeSessions = await _sessions.CountActiveByUserAsync(user.Id);
        if (activeSessions >= maxSessions)
        {
            var oldest = await _sessions.GetOldestActiveByUserAsync(user.Id);
            if (oldest is not null)
                await _sessions.EndSessionAsync(oldest.Id, "replaced");
        }

        // Create session + refresh token in a transaction
        var absoluteTimeout    = Cfg("session_absolute_timeout_minutes", 480);
        var idleTimeoutMinutes = Cfg("session_idle_timeout_minutes", 30);
        var refreshExpiryDays  = Cfg("jwt_refresh_expiry_days", 7);

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
                    ExpiresAt = now.AddMinutes(absoluteTimeout),
                    IdleTimeoutMinutes = idleTimeoutMinutes
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

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken = _jwt.IssueAccessToken(user, session.Id, platformRoles);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

        // Fire all post-login side-effects in parallel
        await Task.WhenAll(
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = user.Id,
                EventType = AuditEventType.LoginSuccess,
                IpAddress = ipAddress,
                UserAgent = userAgent
            }),
            _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = user.Id,
                EventType = AuditEventType.SessionStart,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details = JsonSerializer.Serialize(new { session_id = session.Id })
            }),
            _users.UpdateLastSeenAtAsync(user.Id, now),
            _loginAttempts.RecordAsync(new LoginAttempt
            {
                Email = request.Email,
                IpAddress = ipAddress,
                Success = true,
                AttemptedAt = now
            })
        );

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

    public async Task LogoutAsync(Guid? sessionId, Guid userId, string? ipAddress)
    {
        if (sessionId.HasValue)
            await _sessions.EndSessionAsync(sessionId.Value, "logout");
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
        var config = await LoadConfigAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var tokenHash = _jwt.HashToken(request.RefreshToken);
        var stored = await _refreshTokens.GetByTokenHashAsync(tokenHash);

        if (stored is null || stored.Revoked || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        var user = await _users.GetByIdAsync(stored.UserId)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.Status != "active")
            throw new ForbiddenException($"User account is {user.Status}.");

        var refreshExpiryDays = Cfg("jwt_refresh_expiry_days", 7);

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

        var sessionId = stored.SessionId ?? Guid.Empty;
        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken = _jwt.IssueAccessToken(user, sessionId, platformRoles);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

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

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId, string? appSlug)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(userId);

        IEnumerable<AppAccessDto> appAccess = [];
        if (!string.IsNullOrEmpty(appSlug))
        {
            var context = await _userContext.GetUserContextAsync(userId, appSlug);
            appAccess =
            [
                new AppAccessDto
                {
                    AppSlug = appSlug,
                    RoleName = string.Join(", ", context.Roles),
                    Permissions = context.Permissions
                }
            ];
        }

        return new UserProfileResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            RoleTitle = user.RoleTitle,
            CompanyId = user.CompanyId.ToString(),
            Status = user.Status,
            LastSeenAt = user.LastSeenAt,
            PlatformRoles = platformRoles,
            AppAccess = appAccess
        };
    }

    private async Task<Dictionary<string, string>> LoadConfigAsync()
        => (await _securityConfig.GetAllAsync()).ToDictionary(c => c.ConfigKey, c => c.ConfigValue);
}
