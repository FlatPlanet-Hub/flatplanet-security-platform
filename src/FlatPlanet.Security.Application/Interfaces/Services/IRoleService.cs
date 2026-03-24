using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IRoleService
{
    Task<IEnumerable<RoleResponse>> GetByAppIdAsync(Guid appId);
    Task<RoleResponse> CreateAsync(Guid appId, CreateRoleRequest request);
    Task<RoleResponse> UpdateAsync(Guid appId, Guid id, UpdateRoleRequest request);
    Task DeleteAsync(Guid appId, Guid id);
    Task AssignPermissionAsync(Guid roleId, Guid permissionId, Guid grantedBy);
    Task RemovePermissionAsync(Guid roleId, Guid permissionId);
}
