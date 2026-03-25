using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IPasswordHasher _passwordHasher;

    public UserService(IUserRepository users, IUserAppRoleRepository userAppRoles, IPasswordHasher passwordHasher)
    {
        _users = users;
        _userAppRoles = userAppRoles;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request)
    {
        var user = new Domain.Entities.User
        {
            CompanyId = request.CompanyId,
            Email = request.Email,
            FullName = request.FullName,
            RoleTitle = request.RoleTitle,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Status = "active"
        };
        var created = await _users.CreateAsync(user);
        return MapToResponse(created);
    }

    public async Task<PagedResult<UserResponse>> GetPagedAsync(UserQueryParams query)
    {
        var paged = await _users.GetPagedAsync(query);
        return new PagedResult<UserResponse>
        {
            Items = paged.Items.Select(MapToResponse),
            TotalCount = paged.TotalCount,
            Page = paged.Page,
            PageSize = paged.PageSize
        };
    }

    public async Task<IEnumerable<UserResponse>> GetAllAsync()
    {
        var users = await _users.GetAllAsync();
        return users.Select(MapToResponse);
    }

    public async Task<UserDetailResponse> GetByIdAsync(Guid id)
    {
        var user = await _users.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        var appRoleDetails = await _userAppRoles.GetDetailsByUserIdAsync(id);

        return new UserDetailResponse
        {
            Id = user.Id,
            CompanyId = user.CompanyId,
            Email = user.Email,
            FullName = user.FullName,
            RoleTitle = user.RoleTitle,
            Status = user.Status,
            CreatedAt = user.CreatedAt,
            LastSeenAt = user.LastSeenAt,
            AppAccess = appRoleDetails.Select(d => new UserAppAccessDto
            {
                AppId = d.AppId,
                AppName = d.AppName,
                AppSlug = d.AppSlug,
                RoleName = d.RoleName,
                Status = d.Status,
                GrantedAt = d.GrantedAt,
                ExpiresAt = d.ExpiresAt
            })
        };
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request)
    {
        var user = await _users.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("User not found.");
        user.FullName = request.FullName;
        user.RoleTitle = request.RoleTitle;
        await _users.UpdateAsync(user);
        return MapToResponse(user);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        _ = await _users.GetByIdAsync(id) ?? throw new KeyNotFoundException("User not found.");
        await _users.UpdateStatusAsync(id, status);
    }

    private static UserResponse MapToResponse(Domain.Entities.User u) => new()
    {
        Id = u.Id,
        CompanyId = u.CompanyId,
        Email = u.Email,
        FullName = u.FullName,
        RoleTitle = u.RoleTitle,
        Status = u.Status,
        CreatedAt = u.CreatedAt,
        LastSeenAt = u.LastSeenAt
    };
}
