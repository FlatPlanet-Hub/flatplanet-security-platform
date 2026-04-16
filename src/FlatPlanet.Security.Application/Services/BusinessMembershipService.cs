using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class BusinessMembershipService : IBusinessMembershipService
{
    private readonly IBusinessMembershipRepository _memberships;

    public BusinessMembershipService(IBusinessMembershipRepository memberships)
        => _memberships = memberships;

    public async Task<IEnumerable<MemberResponse>> GetMembersAsync(Guid companyId)
    {
        var members = await _memberships.GetByCompanyIdAsync(companyId);
        return members.Select(Map);
    }

    public async Task AddMemberAsync(Guid companyId, AddMemberRequest request)
    {
        await _memberships.AddAsync(request.UserId, companyId, request.Role);
    }

    public async Task RemoveMemberAsync(Guid companyId, Guid userId)
    {
        await _memberships.RemoveAsync(userId, companyId);
    }

    private static MemberResponse Map(UserBusinessMembership m) => new()
    {
        UserId = m.UserId,
        Email = m.Email ?? string.Empty,
        FullName = m.FullName ?? string.Empty,
        Role = m.Role,
        Status = m.Status,
        JoinedAt = m.JoinedAt
    };
}
