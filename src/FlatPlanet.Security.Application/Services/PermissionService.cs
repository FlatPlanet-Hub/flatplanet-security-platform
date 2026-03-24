using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissions;

    public PermissionService(IPermissionRepository permissions) => _permissions = permissions;

    public async Task<IEnumerable<PermissionResponse>> GetByAppIdAsync(Guid appId)
    {
        var perms = await _permissions.GetByAppIdAsync(appId);
        return perms.Select(Map);
    }

    public async Task<PermissionResponse> CreateAsync(Guid appId, CreatePermissionRequest request)
    {
        var permission = new Permission
        {
            AppId = appId,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category
        };
        var created = await _permissions.CreateAsync(permission);
        return Map(created);
    }

    public async Task<PermissionResponse> UpdateAsync(Guid appId, Guid id, UpdatePermissionRequest request)
    {
        var permission = await _permissions.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Permission not found.");

        if (permission.AppId != appId)
            throw new UnauthorizedAccessException("Permission does not belong to this app.");

        permission.Name = request.Name;
        permission.Description = request.Description;
        permission.Category = request.Category;
        await _permissions.UpdateAsync(permission);
        return Map(permission);
    }

    private static PermissionResponse Map(Permission p) => new()
    {
        Id = p.Id,
        AppId = p.AppId,
        Name = p.Name,
        Description = p.Description,
        Category = p.Category,
        CreatedAt = p.CreatedAt
    };
}
