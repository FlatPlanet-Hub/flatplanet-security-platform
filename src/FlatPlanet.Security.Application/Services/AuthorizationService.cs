using FlatPlanet.Security.Application.DTOs.Authorization;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;

namespace FlatPlanet.Security.Application.Services;

public class AuthorizationService : IAccessAuthorizationService
{
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IAppRepository _apps;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _auditLog;

    public AuthorizationService(
        IUserAppRoleRepository userAppRoles,
        IRolePermissionRepository rolePermissions,
        IAppRepository apps,
        IRoleRepository roles,
        IAuditLogRepository auditLog)
    {
        _userAppRoles = userAppRoles;
        _rolePermissions = rolePermissions;
        _apps = apps;
        _roles = roles;
        _auditLog = auditLog;
    }

    public async Task<AuthorizeResponse> AuthorizeAsync(AuthorizeRequest request, string? ipAddress)
    {
        var app = await _apps.GetBySlugAsync(request.AppSlug)
            ?? throw new KeyNotFoundException($"App '{request.AppSlug}' not found.");

        // 1. Get active, non-expired user app roles
        var userAppRoles = (await _userAppRoles.GetActiveByUserAndAppAsync(request.UserId, app.Id))
            .Where(r => r.Status == "active" && (r.ExpiresAt == null || r.ExpiresAt > DateTime.UtcNow))
            .ToList();

        if (!userAppRoles.Any())
        {
            await LogAuthCheckAsync(request, app.Id, ipAddress, allowed: false);
            return new AuthorizeResponse { Allowed = false };
        }

        // 2. Load role names
        var roleIds = userAppRoles.Select(r => r.RoleId).ToList();
        var roleNames = await _roles.GetNamesByIdsAsync(roleIds);

        // 3. Load permissions for those roles
        var permissions = (await _rolePermissions.GetPermissionsByRoleIdsAsync(roleIds)).ToList();
        var permissionNames = permissions.Select(p => p.Name).Distinct().ToList();

        // 4. Check if required permission is satisfied
        var allowed = permissionNames.Contains(request.RequiredPermission);

        await LogAuthCheckAsync(request, app.Id, ipAddress, allowed);

        return new AuthorizeResponse
        {
            Allowed = allowed,
            Roles = roleNames.ToList(),
            Permissions = permissionNames
        };
    }

    private async Task LogAuthCheckAsync(AuthorizeRequest request, Guid appId, string? ipAddress, bool allowed)
    {
        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = request.UserId,
            AppId = appId,
            EventType = allowed ? "authorize_allowed" : "authorize_denied",
            IpAddress = ipAddress,
            Details = $"{{\"resource\":\"{request.ResourceIdentifier}\",\"permission\":\"{request.RequiredPermission}\"}}"
        });
    }
}
