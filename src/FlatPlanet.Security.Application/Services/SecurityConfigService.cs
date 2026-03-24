using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class SecurityConfigService : ISecurityConfigService
{
    private readonly ISecurityConfigRepository _config;

    public SecurityConfigService(ISecurityConfigRepository config) => _config = config;

    public async Task<IEnumerable<SecurityConfig>> GetAllAsync() =>
        await _config.GetAllAsync();

    public async Task UpdateAsync(string key, string value, Guid updatedBy) =>
        await _config.UpdateAsync(key, value, updatedBy);
}
