using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IUserAccessService
{
    Task<IEnumerable<UserAccessResponse>> GetByAppIdAsync(Guid appId);
    Task<UserAccessResponse> GrantAccessAsync(Guid appId, GrantUserAccessRequest request, Guid grantedBy);
    Task UpdateRoleAsync(Guid appId, Guid userId, Guid roleId);
    Task RevokeAccessAsync(Guid appId, Guid userId);
}
