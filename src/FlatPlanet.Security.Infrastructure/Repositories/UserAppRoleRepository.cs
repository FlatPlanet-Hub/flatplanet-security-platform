using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class UserAppRoleRepository : IUserAppRoleRepository
{
    private readonly IDbConnectionFactory _db;

    public UserAppRoleRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<UserAppRole>> GetActiveByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM user_app_roles WHERE user_id = @UserId AND status = 'active'",
            new { UserId = userId });
    }

    public async Task<IEnumerable<UserAppRole>> GetActiveByUserAndAppAsync(Guid userId, Guid appId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM user_app_roles WHERE user_id = @UserId AND app_id = @AppId AND status = 'active'",
            new { UserId = userId, AppId = appId });
    }

    public async Task<UserAppRole?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<UserAppRole>(
            "SELECT * FROM user_app_roles WHERE id = @Id", new { Id = id });
    }

    public async Task<UserAppRole> CreateAsync(UserAppRole userAppRole)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO user_app_roles (user_id, app_id, role_id, granted_by, expires_at)
            VALUES (@UserId, @AppId, @RoleId, @GrantedBy, @ExpiresAt)
            RETURNING id
            """, userAppRole);
        userAppRole.Id = id;
        return userAppRole;
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE user_app_roles SET status = @Status WHERE id = @Id",
            new { Status = status, Id = id });
    }

    public async Task UpdateRoleAsync(Guid id, Guid roleId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE user_app_roles SET role_id = @RoleId WHERE id = @Id",
            new { RoleId = roleId, Id = id });
    }

    public async Task SuspendAllByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE user_app_roles SET status = 'suspended' WHERE user_id = @UserId AND status = 'active'",
            new { UserId = userId });
    }

    public async Task<IEnumerable<UserAppRole>> GetActiveByAppIdAsync(Guid appId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM user_app_roles WHERE app_id = @AppId AND status = 'active'",
            new { AppId = appId });
    }

    public async Task<bool> HasUsersAssignedAsync(Guid roleId)
    {
        using var conn = await _db.CreateConnectionAsync();
        var count = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM user_app_roles WHERE role_id = @RoleId AND status = 'active'",
            new { RoleId = roleId });
        return count > 0;
    }
}
