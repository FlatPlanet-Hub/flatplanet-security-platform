using System.Data;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface ISessionRepository
{
    Task<Session> CreateAsync(Session session);
    Task<Session> CreateAsync(Session session, IDbConnection conn, IDbTransaction tx);
    Task<Session?> GetByIdAsync(Guid id);
    Task<int> CountActiveByUserAsync(Guid userId);
    Task<Session?> GetOldestActiveByUserAsync(Guid userId);
    Task EndSessionAsync(Guid sessionId, string reason);
    Task EndAllActiveSessionsByUserAsync(Guid userId, string reason);
    Task EndAllActiveSessionsByUserAsync(Guid userId, string reason, IDbConnection conn, IDbTransaction tx);
    Task UpdateLastActiveAtAsync(Guid sessionId, DateTime lastActiveAt);
    Task<IEnumerable<Guid>> GetActiveSessionIdsByUserAsync(Guid userId);
    Task<IEnumerable<Session>> GetAllByUserIdAsync(Guid userId);
    Task EvictOldestIfOverLimitAsync(Guid userId, int maxSessions, IDbConnection conn, IDbTransaction tx);
    Task EndAllActiveSessionsByCompanyIdAsync(Guid companyId, string reason, IDbConnection conn, IDbTransaction tx);
}
