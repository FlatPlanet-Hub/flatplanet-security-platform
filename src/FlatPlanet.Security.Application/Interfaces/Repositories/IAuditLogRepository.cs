using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IAuditLogRepository
{
    Task LogAsync(AuthAuditLog entry);
}
