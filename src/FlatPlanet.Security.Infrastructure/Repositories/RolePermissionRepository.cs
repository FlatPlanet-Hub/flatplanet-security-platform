using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class RolePermissionRepository : IRolePermissionRepository
{
    private readonly IDbConnectionFactory _db;

    public RolePermissionRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Permission>> GetPermissionsByRoleIdsAsync(IEnumerable<Guid> roleIds)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<Permission>(
            """
            SELECT DISTINCT p.* FROM permissions p
            INNER JOIN role_permissions rp ON rp.permission_id = p.id
            WHERE rp.role_id = ANY(@RoleIds)
            """,
            new { RoleIds = roleIds.ToArray() });
    }

    public async Task AssignAsync(Guid roleId, Guid permissionId, Guid grantedBy)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO role_permissions (role_id, permission_id, granted_by)
            VALUES (@RoleId, @PermissionId, @GrantedBy)
            ON CONFLICT (role_id, permission_id) DO NOTHING
            """,
            new { RoleId = roleId, PermissionId = permissionId, GrantedBy = grantedBy });
    }

    public async Task RemoveAsync(Guid roleId, Guid permissionId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM role_permissions WHERE role_id = @RoleId AND permission_id = @PermissionId",
            new { RoleId = roleId, PermissionId = permissionId });
    }
}
