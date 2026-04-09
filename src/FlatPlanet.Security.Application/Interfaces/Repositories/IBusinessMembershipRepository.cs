using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IBusinessMembershipRepository
{
    Task<IEnumerable<UserBusinessMembership>> GetActiveByUserIdAsync(Guid userId);
}
