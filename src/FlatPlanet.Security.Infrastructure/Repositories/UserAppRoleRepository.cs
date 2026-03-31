using Dapper;
using FlatPlanet.Security.Application.DTOs.Access;
using FlatPlanet.Security.Application.DTOs.Users;
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
            "SELECT * FROM user_app_roles WHERE user_id = @UserId AND status = 'active' AND (expires_at IS NULL OR expires_at > now())",
            new { UserId = userId });
    }

    public async Task<IEnumerable<UserAppRole>> GetActiveByUserAndAppAsync(Guid userId, Guid appId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserAppRole>(
            "SELECT * FROM user_app_roles WHERE user_id = @UserId AND app_id = @AppId AND status = 'active' AND (expires_at IS NULL OR expires_at > now())",
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
            INSERT INTO user_app_roles (user_id, app_id, role_id, granted_by, expires_at, status)
            VALUES (@UserId, @AppId, @RoleId, @GrantedBy, @ExpiresAt, 'active')
            ON CONFLICT (app_id, user_id)
            DO UPDATE SET
                role_id    = EXCLUDED.role_id,
                granted_by = EXCLUDED.granted_by,
                expires_at = EXCLUDED.expires_at,
                status     = 'active'
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

    public async Task<IEnumerable<UserAppRoleDetail>> GetDetailsByUserIdAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserAppRoleDetail>(
            """
            SELECT uar.id, uar.user_id, uar.app_id,
                   a.name AS app_name, a.slug AS app_slug,
                   r.name AS role_name,
                   COALESCE(string_agg(p.name, ',' ORDER BY p.name), '') AS permissions,
                   uar.status, uar.granted_at, uar.expires_at
            FROM user_app_roles uar
            JOIN apps a ON a.id = uar.app_id
            JOIN roles r ON r.id = uar.role_id
            LEFT JOIN role_permissions rp ON rp.role_id = r.id
            LEFT JOIN permissions p ON p.id = rp.permission_id
            WHERE uar.user_id = @UserId
              AND uar.status = 'active'
              AND (uar.expires_at IS NULL OR uar.expires_at > now())
            GROUP BY uar.id, uar.user_id, uar.app_id, a.name, a.slug,
                     r.name, uar.status, uar.granted_at, uar.expires_at
            ORDER BY a.name
            """,
            new { UserId = userId });
    }

    public async Task<PagedResult<AccessReviewItemDto>> GetAccessReviewAsync(
        int page, int pageSize, Guid? companyId, Guid? appId)
    {
        var where = new List<string>
        {
            "uar.status = 'active'",
            "(uar.expires_at IS NULL OR uar.expires_at > now())"
        };
        var parameters = new DynamicParameters();
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var safeOffset = (Math.Max(page, 1) - 1) * safePageSize;
        parameters.Add("PageSize", safePageSize);
        parameters.Add("Offset", safeOffset);

        if (companyId.HasValue)
        {
            where.Add("u.company_id = @CompanyId");
            parameters.Add("CompanyId", companyId.Value);
        }
        if (appId.HasValue)
        {
            where.Add("uar.app_id = @AppId");
            parameters.Add("AppId", appId.Value);
        }

        var whereClause = "WHERE " + string.Join(" AND ", where);

        var sql = $"""
            SELECT uar.id AS grant_id, uar.user_id, u.email AS user_email,
                   c.name AS company_name, uar.app_id, a.name AS app_name,
                   r.name AS role_name, uar.granted_at,uar.expires_at,
                   EXTRACT(DAY FROM now() - uar.granted_at)::int AS days_since_granted
            FROM user_app_roles uar
            JOIN users u ON u.id = uar.user_id
            JOIN companies c ON c.id = u.company_id
            JOIN apps a ON a.id = uar.app_id
            JOIN roles r ON r.id = uar.role_id
            {whereClause}
            ORDER BY uar.granted_at ASC
            LIMIT @PageSize OFFSET @Offset
            """;

        var countSql = $"""
            SELECT COUNT(*)
            FROM user_app_roles uar
            JOIN users u ON u.id = uar.user_id
            {whereClause}
            """;

        using var conn = await _db.CreateConnectionAsync();
        var items = await conn.QueryAsync<AccessReviewItemDto>(sql, parameters);
        var total = await conn.QuerySingleAsync<int>(countSql, parameters);

        return new PagedResult<AccessReviewItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = Math.Max(page, 1),
            PageSize = safePageSize
        };
    }
}
