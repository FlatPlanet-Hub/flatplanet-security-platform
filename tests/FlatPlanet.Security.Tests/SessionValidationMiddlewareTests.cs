using System.Security.Claims;
using System.Text.Json;
using FlatPlanet.Security.API.Middleware;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Moq;

namespace FlatPlanet.Security.Tests;

public class SessionValidationMiddlewareTests
{
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IAuditLogRepository> _auditLog = new();

    private HttpContext BuildAuthenticatedContext(Guid sessionId, IServiceProvider services)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("session_id", sessionId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        ], "Bearer");
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = services
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private IServiceProvider BuildServices()
    {
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(ISessionRepository))).Returns(_sessions.Object);
        services.Setup(s => s.GetService(typeof(IAuditLogRepository))).Returns(_auditLog.Object);
        return services.Object;
    }

    [Fact]
    public async Task Request_ShouldPass_WhenSessionActive()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _sessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(new Session
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsActive = true,
            ExpiresAt = now.AddHours(8),
            IdleTimeoutMinutes = 30,
            LastActiveAt = now.AddMinutes(-5)
        });
        _sessions.Setup(s => s.UpdateLastActiveAtAsync(sessionId, It.IsAny<DateTime>())).Returns(Task.CompletedTask);

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = BuildAuthenticatedContext(sessionId, BuildServices());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Request_ShouldReturn401_WhenSessionIdleExpired()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _sessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(new Session
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsActive = true,
            ExpiresAt = now.AddHours(8),
            IdleTimeoutMinutes = 30,
            LastActiveAt = now.AddMinutes(-60) // idle for 60 minutes, timeout is 30
        });
        _sessions.Setup(s => s.EndSessionAsync(sessionId, "idle_timeout")).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var middleware = new SessionValidationMiddleware(_ => Task.CompletedTask);
        var context = BuildAuthenticatedContext(sessionId, BuildServices());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(401, context.Response.StatusCode);
        _sessions.Verify(s => s.EndSessionAsync(sessionId, "idle_timeout"), Times.Once);
    }

    [Fact]
    public async Task Request_ShouldReturn401_WhenSessionAbsoluteExpired()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _sessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(new Session
        {
            Id = sessionId,
            UserId = Guid.NewGuid(),
            IsActive = true,
            ExpiresAt = now.AddHours(-1), // expired 1 hour ago
            IdleTimeoutMinutes = 30,
            LastActiveAt = now.AddMinutes(-5)
        });
        _sessions.Setup(s => s.EndSessionAsync(sessionId, "absolute_timeout")).Returns(Task.CompletedTask);
        _auditLog.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var middleware = new SessionValidationMiddleware(_ => Task.CompletedTask);
        var context = BuildAuthenticatedContext(sessionId, BuildServices());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(401, context.Response.StatusCode);
        _sessions.Verify(s => s.EndSessionAsync(sessionId, "absolute_timeout"), Times.Once);
    }
}
