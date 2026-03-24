using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IUserAppRoleRepository
{
    Task<IEnumerable<UserAppRole>> GetActiveByUserAsync(Guid userId);
    Task<IEnumerable<UserAppRole>> GetActiveByUserAndAppAsync(Guid userId, Guid appId);
    Task<UserAppRole?> GetByIdAsync(Guid id);
    Task<UserAppRole> CreateAsync(UserAppRole userAppRole);
    Task UpdateStatusAsync(Guid id, string status);
    Task UpdateRoleAsync(Guid id, Guid roleId);
    Task SuspendAllByUserAsync(Guid userId);
    Task<bool> HasUsersAssignedAsync(Guid roleId);
}
