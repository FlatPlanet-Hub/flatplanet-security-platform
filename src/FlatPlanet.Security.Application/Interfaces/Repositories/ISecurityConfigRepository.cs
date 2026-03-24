using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface ISecurityConfigRepository
{
    Task<string?> GetValueAsync(string key);
    Task<int> GetIntValueAsync(string key, int defaultValue);
    Task<IEnumerable<SecurityConfig>> GetAllAsync();
    Task UpdateAsync(string key, string value, Guid updatedBy);
}
