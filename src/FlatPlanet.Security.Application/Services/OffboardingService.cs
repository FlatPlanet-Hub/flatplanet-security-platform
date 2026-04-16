using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;

namespace FlatPlanet.Security.Application.Services;

public class OffboardingService : IOffboardingService
{
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IAuditLogRepository _auditLog;
    private readonly IDbConnectionFactory _db;

    public OffboardingService(
        IUserRepository users,
        ISessionRepository sessions,
        IRefreshTokenRepository refreshTokens,
        IUserAppRoleRepository userAppRoles,
        IAuditLogRepository auditLog,
        IDbConnectionFactory db)
    {
        _users = users;
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _userAppRoles = userAppRoles;
        _auditLog = auditLog;
        _db = db;
    }

    public async Task OffboardAsync(Guid userId, Guid requestedBy)
    {
        _ = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            await _users.UpdateStatusAsync(userId, "inactive", conn, tx);
            await _sessions.EndAllActiveSessionsByUserAsync(userId, "offboarded", conn, tx);
            await _refreshTokens.RevokeAllByUserAsync(userId, "offboarded", conn, tx);
            await _userAppRoles.SuspendAllByUserAsync(userId, conn, tx);
            await _auditLog.LogAsync(new AuthAuditLog
            {
                UserId = userId,
                EventType = AuditEventType.UserOffboarded,
                Details = $"{{\"requested_by\":\"{requestedBy}\"}}"
            }, conn, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
