using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IResourceTypeService
{
    Task<IEnumerable<ResourceTypeResponse>> GetAllAsync();
    Task<ResourceTypeResponse> CreateAsync(CreateResourceTypeRequest request);
}
