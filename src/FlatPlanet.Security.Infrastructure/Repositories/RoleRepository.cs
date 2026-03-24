using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly IDbConnectionFactory _db;

    public RoleRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Role?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Role>(
            "SELECT * FROM roles WHERE id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<Role>> GetByAppIdAsync(Guid appId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<Role>(
            "SELECT * FROM roles WHERE app_id = @AppId ORDER BY name",
            new { AppId = appId });
    }

    public async Task<IEnumerable<string>> GetNamesByIdsAsync(IEnumerable<Guid> ids)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<string>(
            "SELECT name FROM roles WHERE id = ANY(@Ids)",
            new { Ids = ids.ToArray() });
    }

    public async Task<Role> CreateAsync(Role role)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO roles (app_id, name, description, is_platform_role)
            VALUES (@AppId, @Name, @Description, @IsPlatformRole)
            RETURNING id
            """, role);
        role.Id = id;
        return role;
    }

    public async Task UpdateAsync(Role role)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE roles SET name = @Name, description = @Description WHERE id = @Id", role);
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM roles WHERE id = @Id", new { Id = id });
    }
}
