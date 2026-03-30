using System.Security.Claims;
using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FlatPlanet.Security.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAdminAuditLogRepository _adminAudit;
    private readonly IHttpContextAccessor _httpContext;

    public UserService(
        IUserRepository users,
        IUserAppRoleRepository userAppRoles,
        IPasswordHasher passwordHasher,
        IAdminAuditLogRepository adminAudit,
        IHttpContextAccessor httpContext)
    {
        _users = users;
        _userAppRoles = userAppRoles;
        _passwordHasher = passwordHasher;
        _adminAudit = adminAudit;
        _httpContext = httpContext;
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request)
    {
        var user = new Domain.Entities.User
        {
            CompanyId    = request.CompanyId,
            Email        = request.Email,
            FullName     = request.FullName,
            RoleTitle    = request.RoleTitle,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Status       = "active"
        };
        var created = await _users.CreateAsync(user);

        await _adminAudit.LogAsync(
            GetActorId(), GetActorEmail(), AdminAction.UserCreate,
            "user", created.Id,
            null,
            new { created.Id, created.Email, created.FullName, created.Status },
            GetIpAddress());

        return MapToResponse(created);
    }

    public async Task<PagedResult<UserResponse>> GetPagedAsync(UserQueryParams query)
    {
        var paged = await _users.GetPagedAsync(query);
        return new PagedResult<UserResponse>
        {
            Items      = paged.Items.Select(MapToResponse),
            TotalCount = paged.TotalCount,
            Page       = paged.Page,
            PageSize   = paged.PageSize
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
            Id         = user.Id,
            CompanyId  = user.CompanyId,
            Email      = user.Email,
            FullName   = user.FullName,
            RoleTitle  = user.RoleTitle,
            Status     = user.Status,
            CreatedAt  = user.CreatedAt,
            LastSeenAt = user.LastSeenAt,
            AppAccess  = appRoleDetails.Select(d => new UserAppAccessDto
            {
                AppId       = d.AppId,
                AppName     = d.AppName,
                AppSlug     = d.AppSlug,
                RoleName    = d.RoleName,
                Permissions = d.Permissions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Status    = d.Status,
                GrantedAt = d.GrantedAt,
                ExpiresAt = d.ExpiresAt
            })
        };
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request)
    {
        var user = await _users.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("User not found.");

        var before = new { user.FullName, user.RoleTitle };
        user.FullName  = request.FullName;
        user.RoleTitle = request.RoleTitle;
        await _users.UpdateAsync(user);

        await _adminAudit.LogAsync(
            GetActorId(), GetActorEmail(), AdminAction.UserUpdate,
            "user", id,
            before,
            new { user.FullName, user.RoleTitle },
            GetIpAddress());

        return MapToResponse(user);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        var user = await _users.GetByIdAsync(id) ?? throw new KeyNotFoundException("User not found.");
        var oldStatus = user.Status;
        await _users.UpdateStatusAsync(id, status);

        var action = status switch
        {
            "suspended" => AdminAction.UserSuspend,
            "inactive"  => AdminAction.UserDeactivate,
            _           => AdminAction.UserUpdate
        };

        await _adminAudit.LogAsync(
            GetActorId(), GetActorEmail(), action,
            "user", id,
            new { status = oldStatus },
            new { status },
            GetIpAddress());
    }

    private Guid GetActorId()
    {
        var sub = _httpContext.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? _httpContext.HttpContext?.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private string GetActorEmail() =>
        _httpContext.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
        ?? _httpContext.HttpContext?.User.FindFirst("email")?.Value
        ?? "unknown";

    private string? GetIpAddress() =>
        _httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static UserResponse MapToResponse(Domain.Entities.User u) => new()
    {
        Id         = u.Id,
        CompanyId  = u.CompanyId,
        Email      = u.Email,
        FullName   = u.FullName,
        RoleTitle  = u.RoleTitle,
        Status     = u.Status,
        CreatedAt  = u.CreatedAt,
        LastSeenAt = u.LastSeenAt
    };
}
