using System.Data;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IPasswordResetTokenRepository
{
    Task CreateAsync(PasswordResetToken token);
    Task<PasswordResetToken?> GetValidByTokenHashAsync(string tokenHash);
    Task MarkAsUsedAsync(Guid tokenId, IDbConnection conn, IDbTransaction tx);
    Task InvalidatePendingByUserAsync(Guid userId);
}
