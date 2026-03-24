using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class ResourceService : IResourceService
{
    private readonly IResourceRepository _resources;
    private readonly IAppRepository _apps;

    public ResourceService(IResourceRepository resources, IAppRepository apps)
    {
        _resources = resources;
        _apps = apps;
    }

    public async Task<IEnumerable<ResourceResponse>> GetByAppIdAsync(Guid appId)
    {
        var resources = await _resources.GetByAppIdAsync(appId);
        return resources.Select(Map);
    }

    public async Task<ResourceResponse> CreateAsync(Guid appId, CreateResourceRequest request)
    {
        _ = await _apps.GetByIdAsync(appId)
            ?? throw new KeyNotFoundException("App not found.");

        var resource = new Resource
        {
            AppId = appId,
            ResourceTypeId = request.ResourceTypeId,
            Name = request.Name,
            Identifier = request.Identifier,
            Status = "active"
        };
        var created = await _resources.CreateAsync(resource);
        return Map(created);
    }

    public async Task<ResourceResponse> UpdateAsync(Guid appId, Guid id, UpdateResourceRequest request)
    {
        var resource = await _resources.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("Resource not found.");

        if (resource.AppId != appId)
            throw new UnauthorizedAccessException("Resource does not belong to this app.");

        resource.Name = request.Name;
        resource.Identifier = request.Identifier;
        resource.Status = request.Status;
        await _resources.UpdateAsync(resource);
        return Map(resource);
    }

    private static ResourceResponse Map(Resource r) => new()
    {
        Id = r.Id,
        AppId = r.AppId,
        ResourceTypeId = r.ResourceTypeId,
        Name = r.Name,
        Identifier = r.Identifier,
        Status = r.Status,
        CreatedAt = r.CreatedAt
    };
}
