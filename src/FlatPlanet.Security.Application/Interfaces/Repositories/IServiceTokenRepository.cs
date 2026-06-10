using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IServiceTokenRepository
{
    Task<ServiceToken?> GetByHashAsync(string tokenHash);
    Task<ServiceToken?> GetByIdAsync(Guid id);
    Task<ServiceToken?> GetByServiceNameAsync(string serviceName);
    Task<IEnumerable<ServiceToken>> GetAllAsync();
    Task<ServiceToken> CreateAsync(ServiceToken token);
    Task UpdateScopesAsync(Guid id, string[] scopes);
    Task RevokeAsync(Guid id, Guid revokedBy);
    Task TouchLastUsedAsync(Guid id);
}
