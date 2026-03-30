using FlatPlanet.Security.Application.DTOs.Admin;
using FlatPlanet.Security.Application.Helpers;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace FlatPlanet.Security.Application.Services;

public class UserAccessService : IUserAccessService
{
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAppRepository _apps;
    private readonly IAdminAuditLogRepository _adminAudit;
    private readonly IHttpContextAccessor _httpContext;

    public UserAccessService(
        IUserAppRoleRepository userAppRoles,
        IUserRepository users,
        IRoleRepository roles,
        IAppRepository apps,
        IAdminAuditLogRepository adminAudit,
        IHttpContextAccessor httpContext)
    {
        _userAppRoles = userAppRoles;
        _users = users;
        _roles = roles;
        _apps = apps;
        _adminAudit = adminAudit;
        _httpContext = httpContext;
    }

    public async Task<IEnumerable<UserAccessResponse>> GetByAppIdAsync(Guid appId)
    {
        var entries = await _userAppRoles.GetActiveByAppIdAsync(appId);
        var result = new List<UserAccessResponse>();

        foreach (var entry in entries)
        {
            var user = await _users.GetByIdAsync(entry.UserId);
            var role = await _roles.GetByIdAsync(entry.RoleId);
            if (user == null || role == null) continue;

            result.Add(new UserAccessResponse
            {
                Id           = entry.Id,
                UserId       = entry.UserId,
                UserEmail    = user.Email,
                UserFullName = user.FullName,
                RoleId       = entry.RoleId,
                RoleName     = role.Name,
                Status       = entry.Status,
                ExpiresAt    = entry.ExpiresAt
            });
        }

        return result;
    }

    public async Task<UserAccessResponse> GrantAccessAsync(Guid appId, GrantUserAccessRequest request, Guid grantedBy)
    {
        _ = await _apps.GetByIdAsync(appId)   ?? throw new KeyNotFoundException("App not found.");
        var user = await _users.GetByIdAsync(request.UserId) ?? throw new KeyNotFoundException("User not found.");
        var role = await _roles.GetByIdAsync(request.RoleId) ?? throw new KeyNotFoundException("Role not found.");

        var entry = new UserAppRole
        {
            UserId    = request.UserId,
            AppId     = appId,
            RoleId    = request.RoleId,
            GrantedBy = grantedBy,
            ExpiresAt = request.ExpiresAt,
            Status    = "active"
        };

        var created = await _userAppRoles.CreateAsync(entry);

        await _adminAudit.LogAsync(
            ActorContext.GetActorId(_httpContext), ActorContext.GetActorEmail(_httpContext), AdminAction.RoleGrant,
            "user_app_role", created.Id,
            null,
            new { userId = request.UserId, appId, roleId = request.RoleId, roleName = role.Name },
            ActorContext.GetIpAddress(_httpContext));

        return new UserAccessResponse
        {
            Id           = created.Id,
            UserId       = created.UserId,
            UserEmail    = user.Email,
            UserFullName = user.FullName,
            RoleId       = created.RoleId,
            RoleName     = role.Name,
            Status       = created.Status,
            ExpiresAt    = created.ExpiresAt
        };
    }

    public async Task UpdateRoleAsync(Guid appId, Guid userId, Guid roleId)
    {
        var entries = await _userAppRoles.GetActiveByUserAndAppAsync(userId, appId);
        var entry = entries.FirstOrDefault()
            ?? throw new KeyNotFoundException("No active access found for this user in this app.");
        await _userAppRoles.UpdateRoleAsync(entry.Id, roleId);
    }

    public async Task RevokeAccessAsync(Guid appId, Guid userId)
    {
        var entries = await _userAppRoles.GetActiveByUserAndAppAsync(userId, appId);
        foreach (var entry in entries)
        {
            await _userAppRoles.UpdateStatusAsync(entry.Id, "revoked");

            await _adminAudit.LogAsync(
                ActorContext.GetActorId(_httpContext), ActorContext.GetActorEmail(_httpContext), AdminAction.RoleRevoke,
                "user_app_role", entry.Id,
                new { userId, appId, roleId = entry.RoleId, status = entry.Status },
                null,
                ActorContext.GetIpAddress(_httpContext));
        }
    }

}
