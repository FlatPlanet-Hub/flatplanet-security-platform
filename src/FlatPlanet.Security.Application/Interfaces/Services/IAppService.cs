using FlatPlanet.Security.Application.DTOs.Admin;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IAppService
{
    Task<IEnumerable<AppResponse>> GetAllAsync();
    Task<AppResponse> GetByIdAsync(Guid id);
    Task<AppResponse> CreateAsync(CreateAppRequest request, Guid registeredBy);
    Task<AppResponse> UpdateAsync(Guid id, UpdateAppRequest request);
}
