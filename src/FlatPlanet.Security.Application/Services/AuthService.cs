using System.Text.Json;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.Common.Helpers;
using FlatPlanet.Security.Application.Common.Options;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IEmailService _emailService;
    private readonly IAppRepository _apps;
    private readonly IMfaService _mfa;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _logger;
    private readonly AppOptions _appOptions;

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
        IPasswordResetTokenRepository resetTokens,
        IEmailService emailService,
        IAppRepository apps,
        IMfaService mfa,
        IMemoryCache cache,
        ILogger<AuthService> logger,
        IOptions<AppOptions> appOptions)
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
        _resetTokens = resetTokens;
        _emailService = emailService;
        _apps = apps;
        _mfa = mfa;
        _cache = cache;
        _logger = logger;
        _appOptions = appOptions.Value;
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

        // MFA gate — intercept before session creation
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
                            UserId          = user.Id,
                            IpAddress       = ipAddress,
                            UserAgent       = userAgent,
                            ExpiresAt       = now.AddMinutes(10),
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
                    RequiresMfa        = true,
                    MfaEnrolmentPending = true,
                    MfaMethod          = "totp",
                    AccessToken        = enrolToken,
                    ExpiresIn          = 600,
                    User               = new UserProfileDto
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

            // email_otp: send challenge and return challenge ID
            var challenge = await _mfa.SendEmailOtpAsync(user.Id, ipAddress);
            return new LoginResponse { RequiresMfa = true, MfaMethod = "email_otp", ChallengeId = challenge.Id, User = new UserProfileDto { UserId = user.Id } };
        }

        // Create session + refresh token in a transaction (with atomic session eviction)
        var maxSessions       = Cfg("max_concurrent_sessions", 3);
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
                await _sessions.EvictOldestIfOverLimitAsync(user.Id, maxSessions, conn, tx);

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
        var accessToken = await _jwt.IssueAccessTokenAsync(user, session.Id, platformRoles);
        var accessExpiryMinutes = Cfg("jwt_access_expiry_minutes", 60);

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
            IdleTimeoutMinutes = idleTimeoutMinutes,
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
        {
            await _sessions.EndSessionAsync(sessionId.Value, "logout");
            _cache.Remove($"fp:sec:session:{sessionId.Value}");
        }
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

        // Case: token not found at all
        if (stored is null)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Strict rotation: any revoked token is immediately invalid — no grace period
        if (stored.Revoked)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Case: token is expired
        if (stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Normal rotation path
        var user = await _users.GetByIdAsync(stored.UserId)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.Status != "active")
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

        if (stored.SessionId.HasValue)
            await _sessions.UpdateLastActiveAtAsync(stored.SessionId.Value, DateTime.UtcNow);

        var sessionId = stored.SessionId ?? Guid.Empty;
        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(user.Id);
        var accessToken = await _jwt.IssueAccessTokenAsync(user, sessionId, platformRoles);
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

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId, string? appSlug)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var platformRoles = await _roles.GetPlatformRoleNamesForUserAsync(userId);

        IEnumerable<AppAccessDto> appAccess = [];
        if (!string.IsNullOrEmpty(appSlug))
        {
            try
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
            catch (ForbiddenException)
            {
                // User has no role in this app — return empty appAccess, not 403
                appAccess = [];
            }
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

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ipAddress)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ArgumentException("Current password is incorrect.");

        var (isValid, errorMessage) = PasswordPolicyValidator.Validate(request.NewPassword);
        if (!isValid)
            throw new ArgumentException(errorMessage);

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("Passwords do not match.");

        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ArgumentException("New password must be different from the current password.");

        var newHash = _passwordHasher.Hash(request.NewPassword);
        await _users.UpdatePasswordHashAsync(userId, newHash);

        try
        {
            var activeSessionIds = await _sessions.GetActiveSessionIdsByUserAsync(userId);
            await Task.WhenAll(
                _sessions.EndAllActiveSessionsByUserAsync(userId, "password_changed"),
                _refreshTokens.RevokeAllByUserAsync(userId, "password_changed")
            );
            foreach (var sid in activeSessionIds)
                _cache.Remove($"fp:sec:session:{sid}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke sessions/tokens after password change for user {UserId}", userId);
        }

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = userId,
            EventType = AuditEventType.PasswordChanged,
            IpAddress = ipAddress
        });
    }

    public async Task<UpdateProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) && string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("At least one field (fullName or email) must be provided.");

        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        // ── Validate everything before any writes ────────────────────────────
        var newName       = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
        var newEmail      = string.IsNullOrWhiteSpace(request.Email)    ? null : request.Email.Trim().ToLowerInvariant();
        var nameChanging  = newName  is not null && newName  != user.FullName;
        var emailChanging = newEmail is not null && newEmail != user.Email.ToLowerInvariant();

        if (emailChanging)
        {
            // GAP-2 fix: GetByEmailAsync is now case-insensitive; this catches all casing variants
            var existing = await _users.GetByEmailAsync(newEmail!);
            if (existing is not null && existing.Id != userId)
                throw new InvalidOperationException("That email address is already in use.");
        }

        // ── Writes (only reached when validation passes) ─────────────────────
        var auditTasks = new List<Task>();

        if (nameChanging)
        {
            await _users.UpdateFullNameAsync(userId, newName!);
            user.FullName = newName!;
            auditTasks.Add(_auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.ProfileNameUpdated,
                IpAddress = ipAddress
            }));
        }

        if (emailChanging)
        {
            await _users.UpdateEmailAsync(userId, newEmail!);
            user.Email = newEmail!;

            // Email is embedded in the JWT claims — revoke all sessions so the stale token
            // cannot be used after the email change. User must log in again.
            try
            {
                var activeSessionIds = await _sessions.GetActiveSessionIdsByUserAsync(userId);
                await Task.WhenAll(
                    _sessions.EndAllActiveSessionsByUserAsync(userId, "email_changed"),
                    _refreshTokens.RevokeAllByUserAsync(userId, "email_changed")
                );
                foreach (var sid in activeSessionIds)
                    _cache.Remove($"fp:sec:session:{sid}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to revoke sessions after email change for user {UserId}", userId);
            }

            auditTasks.Add(_auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = userId,
                EventType = AuditEventType.ProfileEmailUpdated,
                IpAddress = ipAddress
            }));
        }

        if (auditTasks.Count > 0)
            await Task.WhenAll(auditTasks);

        return new UpdateProfileResponse
        {
            FullName        = user.FullName,
            Email           = user.Email,
            RequiresReLogin = emailChanging
        };
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _users.GetByEmailAsync(request.Email);
        if (user is null)
            return;

        // SSO: reset link always uses the platform's configured base URL.
        // There is no per-app routing — the reset-password page is one central location.
        var baseUrl = _appOptions.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("ForgotPasswordAsync: AppOptions.BaseUrl is not configured — reset link cannot be built.");
            return;
        }

        try
        {
            var plain = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();

            await _resetTokens.InvalidatePendingByUserAsync(user.Id);
            await _resetTokens.CreateAsync(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = hash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });

            var link = $"{baseUrl}/reset-password?token={plain}";

            try
            {
                await _emailService.SendPasswordResetEmailAsync(user.Email, link);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
            }

            try
            {
                await _auditLog.LogAsync(new AuthAuditLog
                {
                    UserId = user.Id,
                    EventType = AuditEventType.PasswordResetRequested
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit log failed for PasswordResetRequested user {UserId}", user.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ForgotPasswordAsync failed for user {UserId} — token not created", user.Id);
            // Still return 200 — never reveal whether email exists or not
        }
    }

    public async Task AdminForceResetPasswordAsync(Guid userId, Guid performedByUserId)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        // SSO: reset link always uses the platform's configured base URL.
        var baseUrl = _appOptions.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("AppOptions.BaseUrl is not configured.");

        var plain = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();

        await _resetTokens.InvalidatePendingByUserAsync(user.Id);
        await _resetTokens.CreateAsync(new PasswordResetToken
        {
            UserId    = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });

        var link = $"{baseUrl}/reset-password?token={plain}";

        try
        {
            await _emailService.SendPasswordResetEmailAsync(user.Email, link);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin force-reset: failed to send password reset email to {Email}", user.Email);
        }

        try
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = user.Id,
                EventType = AuditEventType.PasswordResetForcedByAdmin,
                Details   = JsonSerializer.Serialize(new { performed_by = performedByUserId })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log failed for PasswordResetForcedByAdmin user {UserId}", user.Id);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress)
    {
        // Validate inputs FIRST — before any DB call
        var (isValid, errorMessage) = PasswordPolicyValidator.Validate(request.NewPassword);
        if (!isValid)
            throw new ArgumentException(errorMessage);

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("Passwords do not match.");

        // Token lookup — map any DB error to a generic invalid-token response
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Token))).ToLowerInvariant();

        PasswordResetToken token;
        try
        {
            token = await _resetTokens.GetValidByTokenHashAsync(hash)
                ?? throw new ArgumentException("Reset token is invalid or has expired.");
        }
        catch (ArgumentException)
        {
            throw; // re-throw the "invalid or expired" message as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token lookup failed in ResetPasswordAsync");
            throw new ArgumentException("Reset token is invalid or has expired.");
        }

        var user = await _users.GetByIdAsync(token.UserId)
            ?? throw new KeyNotFoundException("User account no longer exists.");

        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ArgumentException("New password must be different from your current password.");

        var newHash = _passwordHasher.Hash(request.NewPassword);

        using (var conn = await _db.CreateConnectionAsync())
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                await _resetTokens.MarkAsUsedAsync(token.Id, conn, tx);
                await _users.UpdatePasswordHashAsync(token.UserId, newHash, conn, tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        try
        {
            var activeSessionIds = await _sessions.GetActiveSessionIdsByUserAsync(token.UserId);
            await Task.WhenAll(
                _sessions.EndAllActiveSessionsByUserAsync(token.UserId, "password_reset"),
                _refreshTokens.RevokeAllByUserAsync(token.UserId, "password_reset")
            );
            foreach (var sid in activeSessionIds)
                _cache.Remove($"fp:sec:session:{sid}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke sessions/tokens after password reset for user {UserId}", token.UserId);
        }

        try
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = token.UserId,
                EventType = AuditEventType.PasswordResetCompleted,
                IpAddress = ipAddress
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log failed for PasswordResetCompleted user {UserId}", token.UserId);
        }
    }

    private async Task<Dictionary<string, string>> LoadConfigAsync()
    {
        const string cacheKey = "fp:sec:cfg:all";
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? cached) && cached is not null)
            return cached;

        var configs = await _securityConfig.GetAllAsync();
        var dict = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue);

        _cache.Set(cacheKey, dict, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        return dict;
    }
}
