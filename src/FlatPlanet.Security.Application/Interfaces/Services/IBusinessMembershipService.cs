using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IBusinessMembershipService
{
    Task<IEnumerable<MemberResponse>> GetMembersAsync(Guid companyId);
    Task AddMemberAsync(Guid companyId, AddMemberRequest request);
    Task RemoveMemberAsync(Guid companyId, Guid userId);
}
