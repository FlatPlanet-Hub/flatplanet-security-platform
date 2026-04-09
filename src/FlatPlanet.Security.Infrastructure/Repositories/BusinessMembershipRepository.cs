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
}
