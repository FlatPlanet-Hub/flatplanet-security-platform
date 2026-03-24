using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Moq;

namespace FlatPlanet.Security.Tests;

public class UserContextServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserAppRoleRepository> _userAppRoles = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IAppRepository> _apps = new();
    private readonly Mock<ICompanyRepository> _companies = new();

    private UserContextService CreateService() => new(
        _users.Object, _userAppRoles.Object, _roles.Object,
        _rolePermissions.Object, _apps.Object, _companies.Object);

    [Fact]
    public async Task GetUserContext_ShouldReturnRolesAndPermissions_WhenUserHasAccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, CompanyId = companyId, Email = "user@test.com", FullName = "Test User", Status = "active" });

        _companies.Setup(c => c.GetByIdAsync(companyId))
            .ReturnsAsync(new Company { Id = companyId, Name = "Acme Corp" });

        _apps.Setup(a => a.GetBySlugAsync("my-app"))
            .ReturnsAsync(new App { Id = appId, Slug = "my-app", Name = "My App" });

        _userAppRoles.Setup(u => u.GetActiveByUserAsync(userId))
            .ReturnsAsync(new[]
            {
                new UserAppRole { Id = Guid.NewGuid(), UserId = userId, AppId = appId, RoleId = roleId, Status = "active" }
            });

        _roles.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[] { "editor" });

        _rolePermissions.Setup(rp => rp.GetPermissionsByRoleIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[] { new Permission { Id = Guid.NewGuid(), Name = "resource:read" } });

        _apps.Setup(a => a.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[] { new App { Id = appId, Slug = "my-app", Name = "My App" } });

        var service = CreateService();

        // Act
        var result = await service.GetUserContextAsync(userId, "my-app");

        // Assert
        Assert.Equal("user@test.com", result.Email);
        Assert.Contains("editor", result.Roles);
        Assert.Contains("resource:read", result.Permissions);
    }

    [Fact]
    public async Task GetUserContext_ShouldReturnAllowedApps()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var app1Id = Guid.NewGuid();
        var app2Id = Guid.NewGuid();

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, CompanyId = companyId, Email = "user@test.com", FullName = "Test", Status = "active" });

        _companies.Setup(c => c.GetByIdAsync(companyId))
            .ReturnsAsync(new Company { Id = companyId, Name = "Acme" });

        _apps.Setup(a => a.GetBySlugAsync("app-one"))
            .ReturnsAsync(new App { Id = app1Id, Slug = "app-one", Name = "App One" });

        _userAppRoles.Setup(u => u.GetActiveByUserAsync(userId))
            .ReturnsAsync(new[]
            {
                new UserAppRole { UserId = userId, AppId = app1Id, RoleId = Guid.NewGuid(), Status = "active" },
                new UserAppRole { UserId = userId, AppId = app2Id, RoleId = Guid.NewGuid(), Status = "active" }
            });

        _roles.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[] { "viewer" });

        _rolePermissions.Setup(rp => rp.GetPermissionsByRoleIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Enumerable.Empty<Permission>());

        _apps.Setup(a => a.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[]
            {
                new App { Id = app1Id, Slug = "app-one", Name = "App One" },
                new App { Id = app2Id, Slug = "app-two", Name = "App Two" }
            });

        var service = CreateService();

        // Act
        var result = await service.GetUserContextAsync(userId, "app-one");

        // Assert
        Assert.Equal(2, result.AllowedApps.Count);
        Assert.Contains(result.AllowedApps, a => a.AppSlug == "app-one");
        Assert.Contains(result.AllowedApps, a => a.AppSlug == "app-two");
    }
}
