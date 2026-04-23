using System.Text.Json;
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

public class PasswordService : IPasswordService
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuditLogRepository _auditLog;
    private readonly IDbConnectionFactory _db;
    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PasswordService> _logger;
    private readonly AppOptions _appOptions;

    public PasswordService(
        IPasswordHasher passwordHasher,
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IAuditLogRepository auditLog,
        IDbConnectionFactory db,
        IPasswordResetTokenRepository resetTokens,
        IEmailService emailService,
        IMemoryCache cache,
        ILogger<PasswordService> logger,
        IOptions<AppOptions> appOptions)
    {
        _passwordHasher = passwordHasher;
        _users          = users;
        _sessions       = sessions;
        _refreshTokens  = refreshTokens;
        _auditLog       = auditLog;
        _db             = db;
        _resetTokens    = resetTokens;
        _emailService   = emailService;
        _cache          = cache;
        _logger         = logger;
        _appOptions     = appOptions.Value;
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
            await RevokeAllSessionsAsync(userId, "password_changed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke sessions/tokens after password change for user {UserId}", userId);
        }

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId    = userId,
            EventType = AuditEventType.PasswordChanged,
            IpAddress = ipAddress
        });
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
            var link = await GenerateAndStoreResetTokenAsync(user, baseUrl);

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
                    UserId    = user.Id,
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

        var link = await GenerateAndStoreResetTokenAsync(user, baseUrl);

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
        var (isValid, errorMessage) = PasswordPolicyValidator.Validate(request.NewPassword);
        if (!isValid)
            throw new ArgumentException(errorMessage);

        if (request.NewPassword != request.ConfirmPassword)
            throw new ArgumentException("Passwords do not match.");

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Token))).ToLowerInvariant();

        PasswordResetToken token;
        try
        {
            token = await _resetTokens.GetValidByTokenHashAsync(hash)
                ?? throw new ArgumentException("Reset token is invalid or has expired.");
        }
        catch (ArgumentException)
        {
            throw;
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
            await RevokeAllSessionsAsync(token.UserId, "password_reset");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke sessions/tokens after password reset for user {UserId}", token.UserId);
        }

        try
        {
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId    = token.UserId,
                EventType = AuditEventType.PasswordResetCompleted,
                IpAddress = ipAddress
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log failed for PasswordResetCompleted user {UserId}", token.UserId);
        }
    }

    private async Task<string> GenerateAndStoreResetTokenAsync(User user, string baseUrl)
    {
        var plain = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash  = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
        await _resetTokens.InvalidatePendingByUserAsync(user.Id);
        await _resetTokens.CreateAsync(new PasswordResetToken
        {
            UserId    = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        return $"{baseUrl}/reset-password?token={plain}";
    }

    private async Task RevokeAllSessionsAsync(Guid userId, string reason)
    {
        var activeSessionIds = await _sessions.GetActiveSessionIdsByUserAsync(userId);
        await Task.WhenAll(
            _sessions.EndAllActiveSessionsByUserAsync(userId, reason),
            _refreshTokens.RevokeAllByUserAsync(userId, reason)
        );
        foreach (var sid in activeSessionIds)
            _cache.Remove($"fp:sec:session:{sid}");
    }
}
