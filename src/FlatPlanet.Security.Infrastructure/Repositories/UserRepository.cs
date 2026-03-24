using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _db;

    public UserRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @Id",
            new { Id = id });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE email = @Email",
            new { Email = email });
    }

    public async Task UpdateLastSeenAtAsync(Guid userId, DateTime lastSeenAt)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET last_seen_at = @LastSeenAt WHERE id = @Id",
            new { LastSeenAt = lastSeenAt, Id = userId });
    }

    public async Task UpdateStatusAsync(Guid userId, string status)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET status = @Status WHERE id = @Id",
            new { Status = status, Id = userId });
    }
}
