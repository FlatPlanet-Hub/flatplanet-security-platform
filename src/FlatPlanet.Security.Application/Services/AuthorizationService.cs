using System.Text.Json;
using FlatPlanet.Security.Application.DTOs.Authorization;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace FlatPlanet.Security.Application.Services;

public class AuthorizationService : IAccessAuthorizationService
{
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IAppRepository _apps;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _auditLog;
    private readonly ISecurityConfigRepository _securityConfig;
    private readonly IMemoryCache _cache;

    public AuthorizationService(
        IUserAppRoleRepository userAppRoles,
        IRolePermissionRepository rolePermissions,
        IAppRepository apps,
        IRoleRepository roles,
        IAuditLogRepository auditLog,
        ISecurityConfigRepository securityConfig,
        IMemoryCache cache)
    {
        _userAppRoles = userAppRoles;
        _rolePermissions = rolePermissions;
        _apps = apps;
        _roles = roles;
        _auditLog = auditLog;
        _securityConfig = securityConfig;
        _cache = cache;
    }

    public async Task<AuthorizeResponse> AuthorizeAsync(Guid userId, AuthorizeRequest request, string? ipAddress)
    {
        var app = await _apps.GetBySlugAsync(request.AppSlug)
            ?? throw new KeyNotFoundException($"App '{request.AppSlug}' not found.");

        // Check cache first (allowed=true results only — denied is never cached)
        var cacheKey = $"fp:sec:authz:{userId}:{request.AppSlug}:{request.RequiredPermission}";
        if (_cache.TryGetValue(cacheKey, out AuthorizeResponse? cachedResponse) && cachedResponse is not null)
            return cachedResponse;

        var userAppRoles = (await _userAppRoles.GetActiveByUserAndAppAsync(userId, app.Id)).ToList();

        if (!userAppRoles.Any())
        {
            await LogAuthCheckAsync(userId, request, app.Id, ipAddress, allowed: false);
            return new AuthorizeResponse { Allowed = false };
        }

        var roleIds = userAppRoles.Select(r => r.RoleId).ToList();
        var roleNames = await _roles.GetNamesByIdsAsync(roleIds);

        var permissions = (await _rolePermissions.GetPermissionsByRoleIdsAsync(roleIds)).ToList();
        var permissionNames = permissions.Select(p => p.Name).Distinct().ToList();

        var allowed = permissionNames.Contains(request.RequiredPermission);

        var response = new AuthorizeResponse
        {
            Allowed = allowed,
            Roles = roleNames.ToList(),
            Permissions = permissionNames
        };

        if (allowed)
        {
            _cache.Set(cacheKey, response, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            });
        }

        await LogAuthCheckAsync(userId, request, app.Id, ipAddress, allowed);

        return response;
    }

    private async Task LogAuthCheckAsync(Guid userId, AuthorizeRequest request, Guid appId, string? ipAddress, bool allowed)
    {
        // Always log denied. Only log allowed if config key audit_log_authorize_allowed=true.
        if (allowed)
        {
            var raw = await _securityConfig.GetValueAsync("audit_log_authorize_allowed");
            if (!string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return;
        }

        await _auditLog.LogAsync(new AuthAuditLog
        {
            UserId = userId,
            AppId = appId,
            EventType = allowed ? AuditEventType.AuthorizeAllowed : AuditEventType.AuthorizeDenied,
            IpAddress = ipAddress,
            Details = JsonSerializer.Serialize(new
            {
                resource = request.ResourceIdentifier,
                permission = request.RequiredPermission
            })
        });
    }
}
