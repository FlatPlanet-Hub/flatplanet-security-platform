using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IResourceRepository
{
    Task<IEnumerable<Resource>> GetByAppIdAsync(Guid appId);
    Task<Resource?> GetByIdAsync(Guid id);
    Task<Resource> CreateAsync(Resource resource);
    Task UpdateAsync(Resource resource);
}
