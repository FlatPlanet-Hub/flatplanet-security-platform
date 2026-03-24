using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Interfaces.Repositories;

public interface IRolePermissionRepository
{
    Task<IEnumerable<Permission>> GetPermissionsByRoleIdsAsync(IEnumerable<Guid> roleIds);
    Task AssignAsync(Guid roleId, Guid permissionId, Guid grantedBy);
    Task RemoveAsync(Guid roleId, Guid permissionId);
}
