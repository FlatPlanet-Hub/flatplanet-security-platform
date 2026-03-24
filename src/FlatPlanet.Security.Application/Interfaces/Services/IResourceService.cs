using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IResourceService
{
    Task<IEnumerable<ResourceResponse>> GetByAppIdAsync(Guid appId);
    Task<ResourceResponse> CreateAsync(Guid appId, CreateResourceRequest request);
    Task<ResourceResponse> UpdateAsync(Guid appId, Guid id, UpdateResourceRequest request);
}
