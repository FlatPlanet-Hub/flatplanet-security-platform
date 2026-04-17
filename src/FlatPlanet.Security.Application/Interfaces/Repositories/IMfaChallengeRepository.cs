using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IMfaChallengeRepository
{
    Task<MfaChallenge> CreateAsync(MfaChallenge challenge);
    Task<MfaChallenge?> GetActiveByUserIdAndTypeAsync(Guid userId, string challengeType);
    Task<MfaChallenge?> GetByIdAsync(Guid id);
    Task MarkVerifiedAsync(Guid id);
    Task IncrementAttemptsAsync(Guid id);
    Task InvalidateActiveByTypeAsync(Guid userId, string challengeType);
    Task DeleteExpiredAsync();
}
