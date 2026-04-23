using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace FlatPlanet.Security.Application.Services;

public class SecurityConfigService : ISecurityConfigService
{
    private const string CacheKey = "fp:sec:cfg:all";
    private readonly ISecurityConfigRepository _config;
    private readonly IMemoryCache _cache;

    public SecurityConfigService(ISecurityConfigRepository config, IMemoryCache cache)
    {
        _config = config;
        _cache  = cache;
    }

    public async Task<IEnumerable<SecurityConfig>> GetAllAsync() =>
        await _config.GetAllAsync();

    public async Task UpdateAsync(string key, string value, Guid updatedBy) =>
        await _config.UpdateAsync(key, value, updatedBy);

    public async Task<Dictionary<string, string>> GetAllCachedAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, string>? cached) && cached is not null)
            return cached;

        var configs = await _config.GetAllAsync();
        var dict = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue);

        _cache.Set(CacheKey, dict, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        return dict;
    }
}
