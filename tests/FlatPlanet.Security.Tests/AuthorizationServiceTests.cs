using FlatPlanet.Security.Application.DTOs.Authorization;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Moq;

namespace FlatPlanet.Security.Tests;

public class AuthorizationServiceTests
{
    private readonly Mock<IUserAppRoleRepository> _userAppRoles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IAppRepository> _apps = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();

    private AuthorizationService CreateService() => new(
        _userAppRoles.Object, _rolePermissions.Object,
        _apps.Object, _roles.Object, _auditLog.Object);

    private readonly Guid _appId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    [Fact]
    public async Task Authorize_ShouldReturnAllowed_WhenPermissionExists()
    {
        // Arrange
        var permissionName = "resource:read";
        _apps.Setup(a => a.GetBySlugAsync("my-app"))
            .ReturnsAsync(new App { Id = _appId, Slug = "my-app", Name = "My App" });

        _userAppRoles.Setup(u => u.GetActiveByUserAndAppAsync(_userId, _appId))
            .ReturnsAsync(new[] { new UserAppRole { Id = Guid.NewGuid(), UserId = _userId, AppId = _appId, RoleId = _roleId, Status = "active" } });

        _roles.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[] { "editor" });

        _rolePermissions.Setup(rp => rp.GetPermissionsByRoleIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new[] { new Permission { Id = Guid.NewGuid(), Name = permissionName } });

        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.AuthorizeAsync(_userId, new AuthorizeRequest
        {
            AppSlug = "my-app",
            ResourceIdentifier = "doc/123",
            RequiredPermission = permissionName
        }, "1.2.3.4");

        // Assert
        Assert.True(result.Allowed);
        Assert.Contains("editor", result.Roles);
        Assert.Contains(permissionName, result.Permissions);
    }

    [Fact]
    public async Task Authorize_ShouldReturnDenied_WhenNoRoleAssigned()
    {
        // Arrange
        _apps.Setup(a => a.GetBySlugAsync("my-app"))
            .ReturnsAsync(new App { Id = _appId, Slug = "my-app", Name = "My App" });

        _userAppRoles.Setup(u => u.GetActiveByUserAndAppAsync(_userId, _appId))
            .ReturnsAsync(Enumerable.Empty<UserAppRole>());

        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.AuthorizeAsync(_userId, new AuthorizeRequest
        {
            AppSlug = "my-app",
            ResourceIdentifier = "doc/123",
            RequiredPermission = "resource:delete"
        }, null);

        // Assert
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Authorize_ShouldReturnDenied_WhenRoleExpired()
    {
        // Arrange — SQL filters expired roles; repo returns empty
        _apps.Setup(a => a.GetBySlugAsync("my-app"))
            .ReturnsAsync(new App { Id = _appId, Slug = "my-app", Name = "My App" });

        _userAppRoles.Setup(u => u.GetActiveByUserAndAppAsync(_userId, _appId))
            .ReturnsAsync(Enumerable.Empty<UserAppRole>());

        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.AuthorizeAsync(_userId, new AuthorizeRequest
        {
            AppSlug = "my-app",
            ResourceIdentifier = "doc/123",
            RequiredPermission = "resource:read"
        }, null);

        // Assert
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Authorize_ShouldReturnDenied_WhenRoleSuspended()
    {
        // Arrange — SQL filters suspended roles; repo returns empty
        _apps.Setup(a => a.GetBySlugAsync("my-app"))
            .ReturnsAsync(new App { Id = _appId, Slug = "my-app", Name = "My App" });

        _userAppRoles.Setup(u => u.GetActiveByUserAndAppAsync(_userId, _appId))
            .ReturnsAsync(Enumerable.Empty<UserAppRole>());

        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var result = await service.AuthorizeAsync(_userId, new AuthorizeRequest
        {
            AppSlug = "my-app",
            ResourceIdentifier = "doc/123",
            RequiredPermission = "resource:read"
        }, null);

        // Assert
        Assert.False(result.Allowed);
    }
}
