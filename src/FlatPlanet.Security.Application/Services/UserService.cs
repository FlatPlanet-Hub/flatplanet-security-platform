using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _users;

    public UserService(IUserRepository users) => _users = users;

    public async Task<IEnumerable<UserResponse>> GetAllAsync()
    {
        var users = await _users.GetAllAsync();
        return users.Select(Map);
    }

    public async Task<UserResponse> GetByIdAsync(Guid id)
    {
        var user = await _users.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("User not found.");
        return Map(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request)
    {
        var user = await _users.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("User not found.");
        user.FullName = request.FullName;
        user.RoleTitle = request.RoleTitle;
        await _users.UpdateAsync(user);
        return Map(user);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        _ = await _users.GetByIdAsync(id) ?? throw new KeyNotFoundException("User not found.");
        await _users.UpdateStatusAsync(id, status);
    }

    private static UserResponse Map(Domain.Entities.User u) => new()
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
