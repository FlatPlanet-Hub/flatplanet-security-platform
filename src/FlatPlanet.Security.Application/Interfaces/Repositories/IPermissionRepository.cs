using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IPermissionRepository
{
    Task<IEnumerable<Permission>> GetByAppIdAsync(Guid appId);
    Task<Permission?> GetByIdAsync(Guid id);
    Task<Permission> CreateAsync(Permission permission);
    Task UpdateAsync(Permission permission);
}
