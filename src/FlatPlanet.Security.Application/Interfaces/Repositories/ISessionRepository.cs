using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface ISessionRepository
{
    Task<Session> CreateAsync(Session session);
    Task<Session?> GetByIdAsync(Guid id);
    Task<int> CountActiveByUserAsync(Guid userId);
    Task<Session?> GetOldestActiveByUserAsync(Guid userId);
    Task EndSessionAsync(Guid sessionId, string reason);
    Task EndAllActiveSessionsByUserAsync(Guid userId, string reason);
    Task UpdateLastActiveAtAsync(Guid sessionId, DateTime lastActiveAt);
    Task<IEnumerable<Session>> GetAllByUserIdAsync(Guid userId);
}
