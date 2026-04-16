using System.Data;
using FlatPlanet.Security.Application.Interfaces;
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
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();
    private readonly Mock<IDbConnectionFactory> _db = new();
    private readonly Mock<IDbConnection> _conn = new();
    private readonly Mock<IDbTransaction> _tx = new();

    private CompanyService CreateService() => new(
        _companies.Object, _users.Object, _userAppRoles.Object,
        _refreshTokens.Object, _sessions.Object, _auditLog.Object, _db.Object);

    private void SetupTransaction()
    {
        _conn.Setup(c => c.BeginTransaction()).Returns(_tx.Object);
        _db.Setup(d => d.CreateConnectionAsync()).ReturnsAsync(_conn.Object);
    }

    [Fact]
    public async Task SuspendCompany_ShouldSuspendAllUsersInTransaction()
    {
        // Arrange
        SetupTransaction();
        var companyId = Guid.NewGuid();
        _companies.Setup(c => c.GetByIdAsync(companyId))
            .ReturnsAsync(new Company { Id = companyId, Name = "Acme", Status = "active" });
        _companies.Setup(c => c.UpdateStatusAsync(companyId, "suspended", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.SuspendByCompanyIdAsync(companyId, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _refreshTokens.Setup(r => r.RevokeAllByCompanyIdAsync(companyId, "company_suspended", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.UpdateStatusAsync(companyId, "suspended");

        // Assert
        _users.Verify(u => u.SuspendByCompanyIdAsync(companyId, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _refreshTokens.Verify(r => r.RevokeAllByCompanyIdAsync(companyId, "company_suspended", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _tx.Verify(t => t.Commit(), Times.Once);
    }

    [Fact]
    public async Task DeactivateCompany_ShouldDeactivateAllUsersInTransaction()
    {
        // Arrange
        SetupTransaction();
        var companyId = Guid.NewGuid();
        _companies.Setup(c => c.GetByIdAsync(companyId))
            .ReturnsAsync(new Company { Id = companyId, Name = "Acme", Status = "active" });
        _companies.Setup(c => c.UpdateStatusAsync(companyId, "inactive", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.DeactivateAllByCompanyIdAsync(companyId, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _userAppRoles.Setup(u => u.SuspendAllByCompanyIdAsync(companyId, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _refreshTokens.Setup(r => r.RevokeAllByCompanyIdAsync(companyId, "company_deactivated", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.EndAllActiveSessionsByCompanyIdAsync(companyId, "company_deactivated", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.UpdateStatusAsync(companyId, "inactive");

        // Assert
        _users.Verify(u => u.DeactivateAllByCompanyIdAsync(companyId, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _userAppRoles.Verify(u => u.SuspendAllByCompanyIdAsync(companyId, It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _refreshTokens.Verify(r => r.RevokeAllByCompanyIdAsync(companyId, "company_deactivated", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _sessions.Verify(s => s.EndAllActiveSessionsByCompanyIdAsync(companyId, "company_deactivated", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
        _tx.Verify(t => t.Commit(), Times.Once);
    }
}
