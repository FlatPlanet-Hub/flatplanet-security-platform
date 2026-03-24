using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbConnectionFactory _db;

    public AuditLogRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task LogAsync(AuthAuditLog entry)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO auth_audit_log (user_id, app_id, event_type, ip_address, user_agent, details)
            VALUES (@UserId, @AppId, @EventType, @IpAddress, @UserAgent, @Details::jsonb)
            """,
            entry);
    }
}
