using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Application.Services;

public class ProfileService : IProfileService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IUserContextService _userContext;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        IUserRepository users,
        IRoleRepository roles,
        IUserContextService userContext,
        IAuditLogRepository auditLog,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IMemoryCache cache,
        ILogger<ProfileService> logger)
    {
        _users         = users;
        _roles         = roles;
        _userContext   = userContext;
        _auditLog      = auditLog;
        _sessions      = sessions;
        _refreshTokens = refreshTokens;
        _cache         = cache;
        _logger        = logger;
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
                        AppSlug     = appSlug,
                        RoleName    = string.Join(", ", context.Roles),
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
            UserId        = user.Id,
            Email         = user.Email,
            FullName      = user.FullName,
            RoleTitle     = user.RoleTitle,
            CompanyId     = user.CompanyId.ToString(),
            Status        = user.Status,
            LastSeenAt    = user.LastSeenAt,
            PlatformRoles = platformRoles,
            AppAccess     = appAccess
        };
    }

    public async Task<UpdateProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) && string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("At least one field (fullName or email) must be provided.");

        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

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
                await RevokeAllSessionsAsync(userId, "email_changed");
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
