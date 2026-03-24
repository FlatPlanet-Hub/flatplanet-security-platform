using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly IDbConnectionFactory _db;

    public SessionRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<Session> CreateAsync(Session session)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO sessions (user_id, app_id, ip_address, user_agent, expires_at)
            VALUES (@UserId, @AppId, @IpAddress, @UserAgent, @ExpiresAt)
            RETURNING id
            """,
            session);
        session.Id = id;
        return session;
    }

    public async Task<Session?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Session>(
            "SELECT * FROM sessions WHERE id = @Id",
            new { Id = id });
    }

    public async Task<int> CountActiveByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sessions WHERE user_id = @UserId AND is_active = true",
            new { UserId = userId });
    }

    public async Task<Session?> GetOldestActiveByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Session>(
            """
            SELECT * FROM sessions
            WHERE user_id = @UserId AND is_active = true
            ORDER BY started_at ASC
            LIMIT 1
            """,
            new { UserId = userId });
    }

    public async Task EndSessionAsync(Guid sessionId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE sessions SET is_active = false, ended_reason = @Reason WHERE id = @Id",
            new { Reason = reason, Id = sessionId });
    }

    public async Task EndAllActiveSessionsByUserAsync(Guid userId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE sessions SET is_active = false, ended_reason = @Reason WHERE user_id = @UserId AND is_active = true",
            new { Reason = reason, UserId = userId });
    }

    public async Task UpdateLastActiveAtAsync(Guid sessionId, DateTime lastActiveAt)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE sessions SET last_active_at = @LastActiveAt WHERE id = @Id",
            new { LastActiveAt = lastActiveAt, Id = sessionId });
    }
}
