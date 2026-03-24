using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IResourceTypeRepository
{
    Task<IEnumerable<ResourceType>> GetAllAsync();
    Task<ResourceType?> GetByIdAsync(Guid id);
    Task<ResourceType> CreateAsync(ResourceType resourceType);
}
