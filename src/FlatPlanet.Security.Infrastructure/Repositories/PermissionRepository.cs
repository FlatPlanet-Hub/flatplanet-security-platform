using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class PermissionRepository : IPermissionRepository
{
    private readonly IDbConnectionFactory _db;

    public PermissionRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Permission>> GetByAppIdAsync(Guid appId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<Permission>(
            "SELECT * FROM permissions WHERE app_id = @AppId ORDER BY category, name",
            new { AppId = appId });
    }

    public async Task<Permission?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Permission>(
            "SELECT * FROM permissions WHERE id = @Id", new { Id = id });
    }

    public async Task<Permission> CreateAsync(Permission permission)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO permissions (app_id, name, description, category)
            VALUES (@AppId, @Name, @Description, @Category)
            RETURNING id
            """, permission);
        permission.Id = id;
        return permission;
    }

    public async Task UpdateAsync(Permission permission)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE permissions SET name = @Name, description = @Description, category = @Category WHERE id = @Id",
            permission);
    }
}
