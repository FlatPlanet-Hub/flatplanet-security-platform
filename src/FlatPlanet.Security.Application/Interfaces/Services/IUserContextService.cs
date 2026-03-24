using FlatPlanet.Security.Application.DTOs.Authorization;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IUserContextService
{
    Task<UserContextResponse> GetUserContextAsync(Guid userId, string appSlug);
}
