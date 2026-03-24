using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task RevokeAsync(Guid tokenId, string reason);
    Task RevokeAllByUserAsync(Guid userId, string reason);
}
