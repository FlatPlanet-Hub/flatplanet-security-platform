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

    public async Task LogAsync(AuthAuditLog entry, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO auth_audit_log (user_id, app_id, event_type, ip_address, user_agent, details)
            VALUES (@UserId, @AppId, @EventType, @IpAddress, @UserAgent, @Details::jsonb)
            """,
            entry,
            transaction: tx);
    }

    public async Task<(IEnumerable<AuthAuditLog> Items, int TotalCount)> QueryAsync(
        Guid? userId, Guid? appId, string? eventType,
        DateTime? from, DateTime? to,
        int page, int pageSize)
    {
        using var conn = await _db.CreateConnectionAsync();

        var where = new List<string>();
        if (userId.HasValue) where.Add("user_id = @UserId");
        if (appId.HasValue) where.Add("app_id = @AppId");
        if (!string.IsNullOrWhiteSpace(eventType)) where.Add("event_type = @EventType");
        if (from.HasValue) where.Add("created_at >= @From");
        if (to.HasValue) where.Add("created_at <= @To");

        var whereClause = where.Any() ? "WHERE " + string.Join(" AND ", where) : "";
        var offset = (page - 1) * pageSize;

        var p = new { UserId = userId, AppId = appId, EventType = eventType, From = from, To = to, Limit = pageSize, Offset = offset };

        var total = await conn.QuerySingleAsync<int>($"SELECT COUNT(*) FROM auth_audit_log {whereClause}", p);
        var items = await conn.QueryAsync<AuthAuditLog>(
            $"SELECT * FROM auth_audit_log {whereClause} ORDER BY created_at DESC LIMIT @Limit OFFSET @Offset", p);

        return (items, total);
    }

    public async Task<IEnumerable<AuthAuditLog>> GetByUserIdAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<AuthAuditLog>(
            "SELECT * FROM auth_audit_log WHERE user_id = @UserId ORDER BY created_at DESC",
            new { UserId = userId });
    }
}
