using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id);
    Task<IEnumerable<Role>> GetByAppIdAsync(Guid appId);
    Task<IEnumerable<string>> GetNamesByIdsAsync(IEnumerable<Guid> ids);
    Task<IEnumerable<string>> GetPlatformRoleNamesForUserAsync(Guid userId);
    Task<Role> CreateAsync(Role role);
    Task UpdateAsync(Role role);
    Task DeleteAsync(Guid id);
}
