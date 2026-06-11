using System.Security.Claims;
using FlatPlanet.Security.Application.Common.Exceptions;
using FlatPlanet.Security.Application.DTOs.Auth;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Constants;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlatPlanet.Security.Tests;

public class FederatedLoginServiceTests
{
    // ── shared test doubles ──────────────────────────────────────────────────

    private readonly Mock<IAzureAdTokenValidator> _validator = new();
    private readonly Mock<IJwtService>            _jwt       = new();
    private readonly Mock<IUserRepository>        _users     = new();
    private readonly Mock<IAppRepository>         _apps      = new();
    private readonly Mock<IUserAppRoleRepository> _grants    = new();
    private readonly Mock<ICompanyRepository>     _companies = new();
    private readonly Mock<IRoleRepository>        _roles     = new();
    private readonly Mock<ISessionRepository>     _sessions  = new();
    private readonly Mock<IRefreshTokenRepository> _tokens   = new();
    private readonly Mock<IAuditLogRepository>    _audit     = new();
    private readonly Mock<ISecurityConfigService> _config    = new();
    private readonly Mock<IDbConnectionFactory>   _db        = new();

    private FederatedLoginService BuildSut() => new(
        _validator.Object,
        _jwt.Object,
        _users.Object,
        _apps.Object,
        _grants.Object,
        _companies.Object,
        _roles.Object,
        _sessions.Object,
        _tokens.Object,
        _audit.Object,
        _config.Object,
        _db.Object,
        NullLogger<FederatedLoginService>.Instance);

    private static User ActiveUser() => new()
    {
        Id        = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Email     = "alice@example.com",
        FullName  = "Alice",
        Status    = EntityStatus.Active
    };

    private static Company ActiveCompany(Guid id) => new()
    {
        Id     = id,
        Name   = "Acme",
        Status = EntityStatus.Active
    };

    private static App FinvoiceApp() => new()
    {
        Id   = Guid.NewGuid(),
        Slug = "finvoice",
        Name = "Finvoice"
    };

    private static ClaimsPrincipal PrincipalWithEmail(string email) =>
        new(new ClaimsIdentity(new[] { new Claim("email", email) }));

    private void SetupHappyPath(User user, App app)
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ReturnsAsync(PrincipalWithEmail(user.Email));

        _users.Setup(u => u.GetByEmailAsync(user.Email))
              .ReturnsAsync(user);

        _companies.Setup(c => c.GetByIdAsync(user.CompanyId))
                  .ReturnsAsync(ActiveCompany(user.CompanyId));

        _apps.Setup(a => a.GetBySlugAsync("finvoice"))
             .ReturnsAsync(app);

        _grants.Setup(g => g.GetActiveByUserAndAppAsync(user.Id, app.Id))
               .ReturnsAsync(new[] { new UserAppRole { Id = Guid.NewGuid(), UserId = user.Id, AppId = app.Id } });

        _roles.Setup(r => r.GetPlatformRoleNamesForUserAsync(user.Id))
              .ReturnsAsync(Array.Empty<string>());

        _config.Setup(c => c.GetAllCachedAsync())
               .ReturnsAsync(new Dictionary<string, string>());

        // Transaction setup
        var connMock = new Mock<System.Data.IDbConnection>();
        var txMock   = new Mock<System.Data.IDbTransaction>();
        connMock.Setup(c => c.BeginTransaction()).Returns(txMock.Object);
        _db.Setup(d => d.CreateConnectionAsync()).ReturnsAsync(connMock.Object);

        _sessions.Setup(s => s.EvictOldestIfOverLimitAsync(It.IsAny<Guid>(), It.IsAny<int>(),
                     It.IsAny<System.Data.IDbConnection>(), It.IsAny<System.Data.IDbTransaction>()))
                 .Returns(Task.CompletedTask);

        _sessions.Setup(s => s.CreateAsync(It.IsAny<Session>(),
                     It.IsAny<System.Data.IDbConnection>(), It.IsAny<System.Data.IDbTransaction>()))
                 .ReturnsAsync(new Session { Id = Guid.NewGuid(), UserId = user.Id });

        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(("plain-token", "hashed-token"));

        _tokens.Setup(t => t.CreateAsync(It.IsAny<RefreshToken>(),
                    It.IsAny<System.Data.IDbConnection>(), It.IsAny<System.Data.IDbTransaction>()))
               .ReturnsAsync(new RefreshToken());

        _jwt.Setup(j => j.IssueAccessTokenAsync(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync("access-token");

        _audit.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);
        _users.Setup(u => u.UpdateLastSeenAtAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).Returns(Task.CompletedTask);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_ValidRequest_ReturnsLoginResponse()
    {
        var user = ActiveUser();
        var app  = FinvoiceApp();
        SetupHappyPath(user, app);

        var sut    = BuildSut();
        var result = await sut.FederatedLoginAsync(
            new FederatedLoginRequest { Provider = "microsoft", IdToken = "valid-token", AppSlug = "finvoice" },
            ipAddress: "1.2.3.4", userAgent: "jest");

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("plain-token",  result.RefreshToken);
        Assert.Equal(user.Email,     result.User.Email);
    }

    // ── provider validation ──────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_UnsupportedProvider_ThrowsUnauthorized()
    {
        // Controller layer is expected to return 400 before this; service uses Unauthorized as a defensive guard.
        var sut = BuildSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "google", IdToken = "t", AppSlug = "finvoice" },
                null, null));
    }

    // ── token validation failure ─────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_InvalidIdToken_ThrowsUnauthorized()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ThrowsAsync(new TokenValidationException("expired"));

        var sut = BuildSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "bad", AppSlug = "finvoice" },
                null, null));
    }

    // ── user not in SP ───────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_UserNotInSP_ThrowsUnauthorized()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ReturnsAsync(PrincipalWithEmail("nobody@example.com"));
        _users.Setup(u => u.GetByEmailAsync("nobody@example.com")).ReturnsAsync((User?)null);
        _audit.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var sut = BuildSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
                null, null));

        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.LoginFailure &&
            l.Details != null && l.Details.Contains("user_not_found"))), Times.Once);
    }

    // ── user suspended ───────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_SuspendedUser_ThrowsForbidden()
    {
        var user = ActiveUser();
        user.Status = EntityStatus.Suspended;

        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ReturnsAsync(PrincipalWithEmail(user.Email));
        _users.Setup(u => u.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _audit.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var sut = BuildSut();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
                null, null));

        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.LoginFailure &&
            l.UserId == user.Id &&
            l.Details != null && l.Details.Contains("user_suspended"))), Times.Once);
    }

    // ── app not found ────────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_AppNotFound_ThrowsUnauthorized()
    {
        var user = ActiveUser();

        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ReturnsAsync(PrincipalWithEmail(user.Email));
        _users.Setup(u => u.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _companies.Setup(c => c.GetByIdAsync(user.CompanyId)).ReturnsAsync(ActiveCompany(user.CompanyId));
        _apps.Setup(a => a.GetBySlugAsync("finvoice")).ReturnsAsync((App?)null);
        _audit.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var sut = BuildSut();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
                null, null));

        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.LoginFailure &&
            l.UserId == user.Id &&
            l.Details != null && l.Details.Contains("app_not_found"))), Times.Once);
    }

    // ── audit log written ────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_Success_WritesAuditLog()
    {
        var user = ActiveUser();
        var app  = FinvoiceApp();
        SetupHappyPath(user, app);

        var sut = BuildSut();
        await sut.FederatedLoginAsync(
            new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
            ipAddress: "1.2.3.4", userAgent: "jest");

        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.FederatedLogin && l.UserId == user.Id)), Times.Once);
    }

    // ── failure audit log ────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_NoAppGrant_WritesLoginFailureAuditLog()
    {
        var user = ActiveUser();
        var app  = FinvoiceApp();

        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ReturnsAsync(PrincipalWithEmail(user.Email));
        _users.Setup(u => u.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _companies.Setup(c => c.GetByIdAsync(user.CompanyId)).ReturnsAsync(ActiveCompany(user.CompanyId));
        _apps.Setup(a => a.GetBySlugAsync("finvoice")).ReturnsAsync(app);
        _grants.Setup(g => g.GetActiveByUserAndAppAsync(user.Id, app.Id))
               .ReturnsAsync(Array.Empty<UserAppRole>());
        _audit.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var sut = BuildSut();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
                null, null));

        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.LoginFailure &&
            l.UserId == user.Id &&
            l.Details != null && l.Details.Contains("no_app_grant"))), Times.Once);
    }

    [Fact]
    public async Task FederatedLoginAsync_ValidatorThrowsHttpRequestException_Propagates()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ThrowsAsync(new HttpRequestException("JWKS fetch failed"));

        var sut = BuildSut();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "bad", AppSlug = "finvoice" },
                null, null));
    }

    // ── company suspended ────────────────────────────────────────────────────

    [Fact]
    public async Task FederatedLoginAsync_SuspendedCompany_ThrowsForbidden()
    {
        var user = ActiveUser();

        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                  .ReturnsAsync(PrincipalWithEmail(user.Email));
        _users.Setup(u => u.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _companies.Setup(c => c.GetByIdAsync(user.CompanyId))
                  .ReturnsAsync(new Company { Id = user.CompanyId, Name = "Acme", Status = EntityStatus.Suspended });
        _audit.Setup(a => a.LogAsync(It.IsAny<AuthAuditLog>())).Returns(Task.CompletedTask);

        var sut = BuildSut();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.FederatedLoginAsync(
                new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
                null, null));

        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.LoginFailure &&
            l.UserId == user.Id &&
            l.Details != null && l.Details.Contains("company_suspended"))), Times.Once);
    }

    // ── P1 #1 regression: email-only claim (no preferred_username) ───────────

    [Fact]
    public async Task FederatedLoginAsync_TokenWithOnlyEmailClaim_ReturnsLoginResponse()
    {
        // Proves AzureAdTokenValidator.MapInboundClaims=false behaviour:
        // the service must find the "email" claim without it being remapped to a long URL.
        var user = ActiveUser();
        var app  = FinvoiceApp();
        SetupHappyPath(user, app);

        // Override validator to return a principal that ONLY has "email" (no preferred_username).
        var emailOnly = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("email", user.Email) }));
        _validator.Setup(v => v.ValidateAsync(It.IsAny<string>())).ReturnsAsync(emailOnly);

        var sut    = BuildSut();
        var result = await sut.FederatedLoginAsync(
            new FederatedLoginRequest { Provider = "microsoft", IdToken = "t", AppSlug = "finvoice" },
            ipAddress: "1.2.3.4", userAgent: "jest");

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(user.Email,     result.User.Email);
    }
}
