using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IAuditLogRepository
{
    Task LogAsync(AuthAuditLog entry);
    Task<(IEnumerable<AuthAuditLog> Items, int TotalCount)> QueryAsync(
        Guid? userId, Guid? appId, string? eventType,
        DateTime? from, DateTime? to,
        int page, int pageSize);
    Task<IEnumerable<AuthAuditLog>> GetByUserIdAsync(Guid userId);
}
