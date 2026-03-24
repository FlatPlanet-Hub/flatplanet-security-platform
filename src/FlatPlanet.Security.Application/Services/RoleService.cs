using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IUserAppRoleRepository _userAppRoles;

    public RoleService(
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IUserAppRoleRepository userAppRoles)
    {
        _roles = roles;
        _rolePermissions = rolePermissions;
        _userAppRoles = userAppRoles;
    }

    public async Task<IEnumerable<RoleResponse>> GetByAppIdAsync(Guid appId)
    {
        var roles = await _roles.GetByAppIdAsync(appId);
        return roles.Select(Map);
    }

    public async Task<RoleResponse> CreateAsync(Guid appId, CreateRoleRequest request)
    {
        var role = new Role
        {
            AppId = appId,
            Name = request.Name,
            Description = request.Description,
            IsPlatformRole = false
        };
        var created = await _roles.CreateAsync(role);
        return Map(created);
    }

    public async Task<RoleResponse> UpdateAsync(Guid appId, Guid id, UpdateRoleRequest request)
    {
        var role = await _roles.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Role not found.");

        if (role.AppId != appId)
            throw new UnauthorizedAccessException("Role does not belong to this app.");

        role.Name = request.Name;
        role.Description = request.Description;
        await _roles.UpdateAsync(role);
        return Map(role);
    }

    public async Task DeleteAsync(Guid appId, Guid id)
    {
        var role = await _roles.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Role not found.");

        if (role.AppId != appId)
            throw new UnauthorizedAccessException("Role does not belong to this app.");

        var hasUsers = await _userAppRoles.HasUsersAssignedAsync(id);
        if (hasUsers)
            throw new InvalidOperationException("Cannot delete a role that has active users assigned.");

        await _roles.DeleteAsync(id);
    }

    public async Task AssignPermissionAsync(Guid roleId, Guid permissionId, Guid grantedBy)
    {
        _ = await _roles.GetByIdAsync(roleId)
            ?? throw new KeyNotFoundException("Role not found.");
        await _rolePermissions.AssignAsync(roleId, permissionId, grantedBy);
    }

    public async Task RemovePermissionAsync(Guid roleId, Guid permissionId)
    {
        await _rolePermissions.RemoveAsync(roleId, permissionId);
    }

    private static RoleResponse Map(Role r) => new()
    {
        Id = r.Id,
        AppId = r.AppId,
        Name = r.Name,
        Description = r.Description,
        IsPlatformRole = r.IsPlatformRole,
        CreatedAt = r.CreatedAt
    };
}
