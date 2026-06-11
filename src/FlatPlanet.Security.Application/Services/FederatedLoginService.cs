using System.Security.Claims;
using System.Text.Json;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Constants;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class FederatedLoginService : IFederatedLoginService
{
    private readonly IAzureAdTokenValidator _tokenValidator;
    private readonly IJwtService _jwt;
    private readonly IUserRepository _users;
    private readonly IAppRepository _apps;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly ICompanyRepository _companies;
    private readonly IRoleRepository _roles;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISecurityConfigService _configService;
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<FederatedLoginService> _logger;

    public FederatedLoginService(
        IAzureAdTokenValidator tokenValidator,
        IJwtService jwt,
        IUserRepository users,
        IAppRepository apps,
        IUserAppRoleRepository userAppRoles,
        ICompanyRepository companies,
        IRoleRepository roles,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog,
        ISecurityConfigService configService,
        IDbConnectionFactory db,
        ILogger<FederatedLoginService> logger)
    {
        _tokenValidator = tokenValidator;
        _jwt            = jwt;
        _users          = users;
        _apps           = apps;
        _userAppRoles   = userAppRoles;
        _companies      = companies;
        _roles          = roles;
        _sessions       = sessions;
        _refreshTokens  = refreshTokens;
        _auditLog       = auditLog;
        _configService  = configService;
        _db             = db;
        _logger         = logger;
    }

    public async Task<LoginResponse> FederatedLoginAsync(FederatedLoginRequest request, string? ipAddress, string? userAgent)
    {
        // Provider is validated at the controller layer (returns 400). Defensive guard only.
        if (!string.Equals(request.Provider, "microsoft", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Unsupported provider '{request.Provider}'.");

        var now = DateTime.UtcNow;

        // 1. Validate the id_token and extract email claim
        ClaimsPrincipal principal;
        try
        {
            principal = await _tokenValidator.ValidateAsync(request.IdToken);
        }
        catch (TokenValidationException ex)
        {
            _logger.LogWarning(ex, "Azure AD id_token validation failed from IP {IpAddress}", ipAddress);
            throw new UnauthorizedAccessException("Invalid or expired identity token.");
        }

        var email = principal.FindFirst("email")?.Value
                 ?? principal.FindFirst("preferred_username")?.Value;
        if (email is null)
        {
            _logger.LogWarning("Azure AD token from IP {IpAddress} validated but contains no email or preferred_username claim", ipAddress);
            throw new UnauthorizedAccessException("Identity token is missing an email claim.");
        }

        // 2. Look up SP user by email (case-insensitive via canonical lowercase)
        var user = await _users.GetByEmailAsync(email.ToLowerInvariant());
        if (user is null)
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { provider = request.Provider, app_slug = request.AppSlug, reason = "user_not_found", email_claim = email })
            });
            throw new UnauthorizedAccessException("No SP account found for this identity. Contact your administrator.");
        }

        if (user.Status != EntityStatus.Active)
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { provider = request.Provider, app_slug = request.AppSlug, reason = "user_suspended" })
            });
            throw new ForbiddenException($"User account is {user.Status}.");
        }

        // 3. Check company active
        var company = await _companies.GetByIdAsync(user.CompanyId)
            ?? throw new UnauthorizedAccessException("Company not found.");
        if (company.Status != EntityStatus.Active)
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { provider = request.Provider, app_slug = request.AppSlug, reason = "company_suspended" })
            });
            throw new ForbiddenException($"Company account is {company.Status}.");
        }

        // 4. Check app-grant for the requested app
        var app = await _apps.GetBySlugAsync(request.AppSlug);
        if (app is null)
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { provider = request.Provider, app_slug = request.AppSlug, reason = "app_not_found" })
            });
            throw new UnauthorizedAccessException($"Application '{request.AppSlug}' not found.");
        }

        var grants = await _userAppRoles.GetActiveByUserAndAppAsync(user.Id, app.Id);
        if (!grants.Any())
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.LoginFailure,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Details   = JsonSerializer.Serialize(new { provider = request.Provider, app_slug = request.AppSlug, reason = "no_app_grant", app_id = app.Id })
            });
            throw new ForbiddenException($"You are not authorised to access '{app.Name}'. Contact your administrator.");
        }

        // 5. Get platform roles for JWT claims
        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);

        // 6. Create session + refresh token in a single transaction
        var config = await _configService.GetAllCachedAsync();
        int Cfg(string key, int def) =>
            config.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        var maxSessions         = Cfg("max_concurrent_sessions", 3);
        var absoluteTimeout     = Cfg("session_absolute_timeout_minutes", 480);
        var idleTimeoutMinutes  = Cfg("session_idle_timeout_minutes", 30);
        var refreshExpiryDays   = Cfg("jwt_refresh_expiry_days", 7);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

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

        var accessToken = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);

        // 7. Audit log — sequential (Dapper connections aren't thread-safe per scope; match LoginService pattern)
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = user.Id,
            EventType = AuditEventType.FederatedLogin,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details   = JsonSerializer.Serialize(new { provider = request.Provider, app_slug = request.AppSlug })
        });
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = user.Id,
            EventType = AuditEventType.SessionStart,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Details   = JsonSerializer.Serialize(new { session_id = session.Id })
        });
        await _users.UpdateLastSeenAtAsync(user.Id, now);

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
}
