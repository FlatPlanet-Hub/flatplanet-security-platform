using FlatPlanet.Security.Application.DTOs.Access;
using FlatPlanet.Security.Application.DTOs.Users;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IAccessReviewService
{
    Task<PagedResult<AccessReviewItemDto>> GetAccessReviewAsync(int page, int pageSize, Guid? companyId, Guid? appId);
}
