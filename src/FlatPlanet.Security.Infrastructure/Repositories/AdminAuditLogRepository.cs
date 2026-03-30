using System.Text.Json;
using Dapper;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class AdminAuditLogRepository : IAdminAuditLogRepository
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<AdminAuditLogRepository> _logger;

    public AdminAuditLogRepository(IDbConnectionFactory db, ILogger<AdminAuditLogRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(Guid actorId, string actorEmail, string action,
                               string targetType, Guid? targetId,
                               object? before, object? after, string? ipAddress)
    {
        try
        {
            using var conn = await _db.CreateConnectionAsync();
            await conn.ExecuteAsync(
                """
                INSERT INTO admin_audit_log
                    (actor_id, actor_email, action, target_type, target_id,
                     before_state, after_state, ip_address)
                VALUES
                    (@ActorId, @ActorEmail, @Action, @TargetType, @TargetId,
                     @BeforeState::jsonb, @AfterState::jsonb, @IpAddress)
                """,
                new
                {
                    ActorId     = actorId,
                    ActorEmail  = actorEmail,
                    Action      = action,
                    TargetType  = targetType,
                    TargetId    = targetId,
                    BeforeState = before is null ? null : JsonSerializer.Serialize(before),
                    AfterState  = after  is null ? null : JsonSerializer.Serialize(after),
                    IpAddress   = ipAddress
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AUDIT FAILURE: {Action} on {TargetType} {TargetId}", action, targetType, targetId);
            // Never rethrow — audit failure must not break the main request flow
        }
    }

    public async Task DeleteExpiredAsync(int retentionDays)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM admin_audit_log WHERE created_at < now() - make_interval(days => @RetentionDays)",
            new { RetentionDays = retentionDays });
    }

    public async Task<IEnumerable<AdminAuditLogDto>> GetPagedAsync(AdminAuditLogQueryParams query)
    {
        using var conn = await _db.CreateConnectionAsync();

        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Action)) where.Add("action = @Action");
        if (!string.IsNullOrWhiteSpace(query.TargetType)) where.Add("target_type = @TargetType");
        if (query.ActorId.HasValue) where.Add("actor_id = @ActorId");
        if (query.From.HasValue) where.Add("created_at >= @From");
        if (query.To.HasValue) where.Add("created_at <= @To");

        var whereClause = where.Any() ? "WHERE " + string.Join(" AND ", where) : "";
        var offset = (query.Page - 1) * query.PageSize;

        return await conn.QueryAsync<AdminAuditLogDto>(
            $"""
            SELECT id, actor_email, action, target_type, target_id, created_at
            FROM admin_audit_log
            {whereClause}
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """,
            new
            {
                query.Action,
                query.TargetType,
                query.ActorId,
                query.From,
                query.To,
                Limit  = query.PageSize,
                Offset = offset
            });
    }

    public async Task<AdminAuditLogDetailDto?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<AdminAuditLogDetailDto>(
            """
            SELECT id, actor_email, action, target_type, target_id,
                   before_state, after_state, ip_address, created_at
            FROM admin_audit_log
            WHERE id = @Id
            """,
            new { Id = id });
    }
}
