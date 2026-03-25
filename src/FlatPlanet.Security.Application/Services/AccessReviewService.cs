using FlatPlanet.Security.Application.DTOs.Access;
using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Application.Services;

public class AccessReviewService : IAccessReviewService
{
    private readonly IUserAppRoleRepository _userAppRoles;

    public AccessReviewService(IUserAppRoleRepository userAppRoles)
    {
        _userAppRoles = userAppRoles;
    }

    public Task<PagedResult<AccessReviewItemDto>> GetAccessReviewAsync(
        int page, int pageSize, Guid? companyId, Guid? appId)
    {
        return _userAppRoles.GetAccessReviewAsync(page, pageSize, companyId, appId);
    }
}
