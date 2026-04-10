using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class BusinessMembershipRepository : IBusinessMembershipRepository
{
    private readonly IDbConnectionFactory _db;

    public BusinessMembershipRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<UserBusinessMembership>> GetActiveByUserIdAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserBusinessMembership>(
            """
            SELECT ubm.*, c.code AS business_code, c.name AS business_name
            FROM user_business_memberships ubm
            JOIN companies c ON c.id = ubm.company_id
            WHERE ubm.user_id = @userId::uuid
              AND ubm.status = 'active'
              AND (ubm.expires_at IS NULL OR ubm.expires_at > NOW())
            """,
            new { userId });
    }

    public async Task<IEnumerable<UserBusinessMembership>> GetByCompanyIdAsync(Guid companyId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<UserBusinessMembership>(
            """
            SELECT ubm.*, u.email, u.full_name
            FROM user_business_memberships ubm
            JOIN users u ON u.id = ubm.user_id
            WHERE ubm.company_id = @companyId::uuid
            ORDER BY ubm.joined_at DESC
            """,
            new { companyId });
    }

    public async Task AddAsync(Guid userId, Guid companyId, string role)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO user_business_memberships (user_id, company_id, role, status)
            VALUES (@userId::uuid, @companyId::uuid, @role, 'active')
            ON CONFLICT (user_id, company_id) DO UPDATE SET status = 'active', role = @role
            """,
            new { userId, companyId, role });
    }

    public async Task RemoveAsync(Guid userId, Guid companyId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE user_business_memberships
            SET status = 'suspended'
            WHERE user_id = @userId::uuid AND company_id = @companyId::uuid
            """,
            new { userId, companyId });
    }
}
