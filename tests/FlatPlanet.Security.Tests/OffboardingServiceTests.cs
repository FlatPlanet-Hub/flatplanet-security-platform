using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Moq;

namespace FlatPlanet.Security.Tests;

public class OffboardingServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IUserAppRoleRepository> _userAppRoles = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();

    private OffboardingService CreateService() => new(
        _users.Object, _sessions.Object, _refreshTokens.Object,
        _userAppRoles.Object, _auditLog.Object);

    [Fact]
    public async Task Offboard_ShouldRevokeAllSessionsAndRoles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();

        _users.Setup(u => u.GetByIdAsync(userId))
            .ReturnsAsync(new User { Id = userId, Email = "user@test.com", Status = "active" });
        _users.Setup(u => u.UpdateStatusAsync(userId, "inactive")).Returns(Task.CompletedTask);
        _sessions.Setup(s => s.EndAllActiveSessionsByUserAsync(userId, "offboarded")).Returns(Task.CompletedTask);
        _refreshTokens.Setup(r => r.RevokeAllByUserAsync(userId, "offboarded")).Returns(Task.CompletedTask);
        _userAppRoles.Setup(u => u.SuspendAllByUserAsync(userId)).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.OffboardAsync(userId, requestedBy);

        // Assert — all 5 steps executed
        _users.Verify(u => u.UpdateStatusAsync(userId, "inactive"), Times.Once);
        _sessions.Verify(s => s.EndAllActiveSessionsByUserAsync(userId, "offboarded"), Times.Once);
        _refreshTokens.Verify(r => r.RevokeAllByUserAsync(userId, "offboarded"), Times.Once);
        _userAppRoles.Verify(u => u.SuspendAllByUserAsync(userId), Times.Once);
        _auditLog.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l => l.EventType == "user_offboarded")), Times.Once);
    }
}
