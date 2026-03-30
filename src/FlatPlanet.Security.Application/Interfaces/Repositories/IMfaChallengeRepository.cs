using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IMfaChallengeRepository
{
    Task<MfaChallenge> CreateAsync(MfaChallenge challenge);
    Task<MfaChallenge?> GetActiveByUserIdAsync(Guid userId);
    Task<MfaChallenge?> GetByIdAsync(Guid id);
    Task MarkVerifiedAsync(Guid id);
    Task IncrementAttemptsAsync(Guid id);
    Task InvalidateActiveAsync(Guid userId);
    Task<bool> HasVerifiedChallengeAsync(Guid userId);
    Task DeleteExpiredAsync();
}
