using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Users;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IUserService
{
    Task<PagedResult<UserResponse>> GetPagedAsync(UserQueryParams query);
    Task<IEnumerable<UserResponse>> GetAllAsync();
    Task<UserDetailResponse> GetByIdAsync(Guid id);
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request);
    Task UpdateStatusAsync(Guid id, string status);
}
