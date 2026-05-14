using FlatPlanet.Security.Application.DTOs.Access;
using FlatPlanet.Security.Application.DTOs.Users;
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
    Task SuspendAllByUserAsync(Guid userId, System.Data.IDbConnection conn, System.Data.IDbTransaction tx);
    Task SuspendAllByCompanyIdAsync(Guid companyId, System.Data.IDbConnection conn, System.Data.IDbTransaction tx);
    Task<IEnumerable<UserAppRole>> GetActiveByAppIdAsync(Guid appId);
    Task<bool> HasUsersAssignedAsync(Guid roleId);
    Task<IEnumerable<UserAppRoleDetail>> GetDetailsByUserIdAsync(Guid userId);
    Task<PagedResult<AccessReviewItemDto>> GetAccessReviewAsync(int page, int pageSize, Guid? companyId, Guid? appId);
}
