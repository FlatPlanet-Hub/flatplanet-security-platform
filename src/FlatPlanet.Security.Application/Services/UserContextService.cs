using FlatPlanet.Security.Application.DTOs.Authorization;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;

namespace FlatPlanet.Security.Application.Services;

public class UserContextService : IUserContextService
{
    private readonly IUserRepository _users;
    private readonly IUserAppRoleRepository _userAppRoles;
    private readonly IRoleRepository _roles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IAppRepository _apps;
    private readonly ICompanyRepository _companies;

    public UserContextService(
        IUserRepository users,
        IUserAppRoleRepository userAppRoles,
        IRoleRepository roles,
        IRolePermissionRepository rolePermissions,
        IAppRepository apps,
        ICompanyRepository companies)
    {
        _users = users;
        _userAppRoles = userAppRoles;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _apps = apps;
        _companies = companies;
    }

    public async Task<UserContextResponse> GetUserContextAsync(Guid userId, string appSlug)
    {
        var user = await _users.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var company = await _companies.GetByIdAsync(user.CompanyId)
            ?? throw new KeyNotFoundException("Company not found.");

        var app = await _apps.GetBySlugAsync(appSlug)
            ?? throw new KeyNotFoundException($"App '{appSlug}' not found.");

        // All active non-expired roles across all apps (for allowedApps)
        var allUserRoles = (await _userAppRoles.GetActiveByUserAsync(userId))
            .Where(r => r.Status == "active" && (r.ExpiresAt == null || r.ExpiresAt > DateTime.UtcNow))
            .ToList();

        // Roles for the requested app
        var appRoleIds = allUserRoles
            .Where(r => r.AppId == app.Id)
            .Select(r => r.RoleId)
            .ToList();

        var appRoleNames = appRoleIds.Any()
            ? await _roles.GetNamesByIdsAsync(appRoleIds)
            : Enumerable.Empty<string>();

        var permissions = appRoleIds.Any()
            ? (await _rolePermissions.GetPermissionsByRoleIdsAsync(appRoleIds)).Select(p => p.Name).Distinct().ToList()
            : new List<string>();

        // Allowed apps = all apps the user has active roles in
        var allowedAppIds = allUserRoles.Select(r => r.AppId).Distinct().ToList();
        var allowedApps = (await _apps.GetByIdsAsync(allowedAppIds))
            .Select(a => new AllowedAppDto { AppId = a.Id, AppSlug = a.Slug, AppName = a.Name })
            .ToList();

        return new UserContextResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            CompanyName = company.Name,
            Roles = appRoleNames.ToList(),
            Permissions = permissions,
            AllowedApps = allowedApps
        };
    }
}
