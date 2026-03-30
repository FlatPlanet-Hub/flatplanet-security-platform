using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IAdminAuditLogRepository
{
    Task LogAsync(Guid actorId, string actorEmail, string action,
                  string targetType, Guid? targetId,
                  object? before, object? after, string? ipAddress);

    Task DeleteExpiredAsync(int retentionDays);

    Task<IEnumerable<AdminAuditLogDto>> GetPagedAsync(AdminAuditLogQueryParams query);

    Task<AdminAuditLogDetailDto?> GetByIdAsync(Guid id);
}
