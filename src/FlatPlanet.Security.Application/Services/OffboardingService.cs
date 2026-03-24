using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class OffboardingService : IOffboardingService
{
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IAuditLogRepository _auditLog;

    public OffboardingService(
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IUserAppRoleRepository userAppRoles,
        IAuditLogRepository auditLog)
    {
        _users = users;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _userAppRoles = userAppRoles;
        _auditLog = auditLog;
    }

    public async Task OffboardAsync(Guid userId, Guid requestedBy)
    {
        _ = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        // Step 1: Set user status = inactive
        await _users.UpdateStatusAsync(userId, "inactive");

        // Step 2: Revoke all active sessions
        await _sessions.EndAllActiveSessionsByUserAsync(userId, "offboarded");

        // Step 3: Revoke all refresh tokens
        await _refreshTokens.RevokeAllByUserAsync(userId, "offboarded");

        // Step 4: Suspend all user_app_roles
        await _userAppRoles.SuspendAllByUserAsync(userId);

        // Step 5: Log to audit trail
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = userId,
            EventType = "user_offboarded",
            Details = $"{{\"requested_by\":\"{requestedBy}\"}}"
        });
    }
}
