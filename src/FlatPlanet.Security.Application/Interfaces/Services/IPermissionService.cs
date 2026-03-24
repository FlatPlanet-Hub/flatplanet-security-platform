using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IPermissionService
{
    Task<IEnumerable<PermissionResponse>> GetByAppIdAsync(Guid appId);
    Task<PermissionResponse> CreateAsync(Guid appId, CreatePermissionRequest request);
    Task<PermissionResponse> UpdateAsync(Guid appId, Guid id, UpdatePermissionRequest request);
}
