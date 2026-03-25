using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Moq;

namespace FlatPlanet.Security.Tests;

public class CompanyServiceTests
{
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserAppRoleRepository> _userAppRoles = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();

    private CompanyService CreateService() => new(
        _companies.Object, _users.Object, _userAppRoles.Object,
        _refreshTokens.Object, _auditLog.Object);

    [Fact]
    public async Task SuspendCompany_ShouldSuspendAllUsers()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _companies.Setup(c => c.GetByIdAsync(companyId))
            .ReturnsAsync(new Company { Id = companyId, Name = "Acme", Status = "active" });
        _companies.Setup(c => c.UpdateStatusAsync(companyId, "suspended")).Returns(Task.CompletedTask);
        _users.Setup(u => u.SuspendByCompanyIdAsync(companyId)).Returns(Task.CompletedTask);
        _refreshTokens.Setup(r => r.RevokeAllByCompanyIdAsync(companyId, "company_suspended")).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.UpdateStatusAsync(companyId, "suspended");

        // Assert
        _users.Verify(u => u.SuspendByCompanyIdAsync(companyId), Times.Once);
    }

    [Fact]
    public async Task SuspendCompany_ShouldRevokeAllRefreshTokens()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        _companies.Setup(c => c.GetByIdAsync(companyId))
            .ReturnsAsync(new Company { Id = companyId, Name = "Acme", Status = "active" });
        _companies.Setup(c => c.UpdateStatusAsync(companyId, "suspended")).Returns(Task.CompletedTask);
        _users.Setup(u => u.SuspendByCompanyIdAsync(companyId)).Returns(Task.CompletedTask);
        _refreshTokens.Setup(r => r.RevokeAllByCompanyIdAsync(companyId, "company_suspended")).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.UpdateStatusAsync(companyId, "suspended");

        // Assert
        _refreshTokens.Verify(r => r.RevokeAllByCompanyIdAsync(companyId, "company_suspended"), Times.Once);
    }
}
