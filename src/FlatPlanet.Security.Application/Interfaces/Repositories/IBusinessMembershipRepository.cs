using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IBusinessMembershipRepository
{
    Task<IEnumerable<UserBusinessMembership>> GetActiveByUserIdAsync(Guid userId);
    Task<IEnumerable<UserBusinessMembership>> GetByCompanyIdAsync(Guid companyId);
    Task AddAsync(Guid userId, Guid companyId, string role);
    Task RemoveAsync(Guid userId, Guid companyId);
}
