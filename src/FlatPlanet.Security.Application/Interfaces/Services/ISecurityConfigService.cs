using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface ISecurityConfigService
{
    Task<IEnumerable<SecurityConfig>> GetAllAsync();
    Task UpdateAsync(string key, string value, Guid updatedBy);
    Task<Dictionary<string, string>> GetAllCachedAsync();
}
