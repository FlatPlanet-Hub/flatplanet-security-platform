using System.Data;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token);
    Task<RefreshToken> CreateAsync(RefreshToken token, IDbConnection conn, IDbTransaction tx);
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task RevokeAsync(Guid tokenId, string reason);
    Task RevokeAllByUserAsync(Guid userId, string reason);
    Task RevokeAllByUserAsync(Guid userId, string reason, IDbConnection conn, IDbTransaction tx);
    Task RevokeAllByCompanyIdAsync(Guid companyId, string reason);
    Task RevokeAllByCompanyIdAsync(Guid companyId, string reason, IDbConnection conn, IDbTransaction tx);
    Task RotateAsync(Guid tokenId, string newTokenHash);
}
