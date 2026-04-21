using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IAppRepository
{
    Task<App?> GetByIdAsync(Guid id);
    Task<App?> GetBySlugAsync(string slug);
    Task<IEnumerable<App>> GetAllAsync();
    Task<IEnumerable<App>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<App> CreateAsync(App app);
    Task UpdateAsync(App app);
    Task UpdateSlugAsync(Guid id, string newSlug);
    Task DeleteAsync(Guid id);
}
