using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Application.Services;

public class ResourceTypeService : IResourceTypeService
{
    private readonly IResourceTypeRepository _resourceTypes;

    public ResourceTypeService(IResourceTypeRepository resourceTypes) => _resourceTypes = resourceTypes;

    public async Task<IEnumerable<ResourceTypeResponse>> GetAllAsync()
    {
        var types = await _resourceTypes.GetAllAsync();
        return types.Select(Map);
    }

    public async Task<ResourceTypeResponse> CreateAsync(CreateResourceTypeRequest request)
    {
        var resourceType = new ResourceType
        {
            Name = request.Name,
            Description = request.Description
        };
        var created = await _resourceTypes.CreateAsync(resourceType);
        return Map(created);
    }

    private static ResourceTypeResponse Map(ResourceType rt) => new()
    {
        Id = rt.Id,
        Name = rt.Name,
        Description = rt.Description,
        CreatedAt = rt.CreatedAt
    };
}
