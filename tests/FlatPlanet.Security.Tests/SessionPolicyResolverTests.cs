using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Constants;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlatPlanet.Security.Tests;

public class SessionPolicyResolverTests
{
    private readonly Mock<IAppRepository> _apps = new();
    private readonly Mock<IUserAppRoleRepository> _grants = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Guid _userId = Guid.NewGuid();

    private SessionPolicyResolver CreateSut() =>
        new(_apps.Object, _grants.Object, _audit.Object, NullLogger<SessionPolicyResolver>.Instance);

    private static Dictionary<string, string> PlatformConfig(
        string absolute = "480", string idle = "30") => new()
    {
        ["session_absolute_timeout_minutes"] = absolute,
        ["session_idle_timeout_minutes"]     = idle,
    };

    private App SetupApp(
        string slug,
        int? absolute = null,
        int? idle = null,
        string status = EntityStatus.Active,
        bool granted = true)
    {
        var app = new App
        {
            Id     = Guid.NewGuid(),
            Slug   = slug,
            Status = status,
            SessionAbsoluteTimeoutMinutes = absolute,
            SessionIdleTimeoutMinutes     = idle
        };
        _apps.Setup(a => a.GetBySlugAsync(slug)).ReturnsAsync(app);
        _grants.Setup(g => g.GetActiveByUserAndAppAsync(_userId, app.Id))
            .ReturnsAsync(granted
                ? new[] { new UserAppRole { Id = Guid.NewGuid(), UserId = _userId, AppId = app.Id } }
                : Array.Empty<UserAppRole>());
        return app;
    }

    // ── No slug / unknown slug ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_ShouldUsePlatformDefaults_WhenSlugIsAbsent(string? slug)
    {
        var result = await CreateSut().ResolveAsync(_userId, slug, PlatformConfig());

        Assert.Null(result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
        _apps.Verify(a => a.GetBySlugAsync(It.IsAny<string>()), Times.Never);
        _grants.Verify(g => g.GetActiveByUserAndAppAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_ShouldUsePlatformDefaults_WhenAppNotFound()
    {
        _apps.Setup(a => a.GetBySlugAsync("does-not-exist")).ReturnsAsync((App?)null);

        var result = await CreateSut().ResolveAsync(_userId, "does-not-exist", PlatformConfig());

        Assert.Null(result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    // ── The grant gate (C1) ──────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ShouldRefuseOverride_WhenUserHasNoGrantToApp()
    {
        // The slug is unauthenticated client input. Without this gate any account could
        // claim the dashboard's 365-day session lifetime just by sending its slug.
        SetupApp("tala-v2-dashboard", absolute: 525600, idle: 525600, granted: false);

        var result = await CreateSut().ResolveAsync(_userId, "tala-v2-dashboard", PlatformConfig());

        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
        // An unverified claim must NOT be written to sessions.app_id — that column means
        // "this session belongs to that app", and an attacker can put anything in the slug.
        Assert.Null(result.AppId);
        VerifyDeniedAuditLogged("no_app_grant");
    }

    [Theory]
    [InlineData(EntityStatus.Suspended)]
    [InlineData(EntityStatus.Inactive)]
    public async Task Resolve_ShouldRefuseOverride_WhenAppIsNotActive(string status)
    {
        SetupApp("tala-v2-dashboard", absolute: 525600, idle: 525600, status: status);

        var result = await CreateSut().ResolveAsync(_userId, "tala-v2-dashboard", PlatformConfig());

        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
        Assert.Null(result.AppId);
        VerifyDeniedAuditLogged("app_inactive");
        // No point checking grants once the app itself is out of service.
        _grants.Verify(g => g.GetActiveByUserAndAppAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>Asserts a session_policy_denied audit entry was written with the given reason.</summary>
    private void VerifyDeniedAuditLogged(string reason) =>
        _audit.Verify(a => a.LogAsync(It.Is<AuthAuditLog>(l =>
            l.EventType == AuditEventType.SessionPolicyDenied &&
            l.UserId == _userId &&
            l.Details != null && l.Details.Contains(reason))), Times.Once);

    [Fact]
    public async Task Resolve_ShouldApplyOverride_WhenAppActiveAndUserGranted()
    {
        var app = SetupApp("tala-v2-dashboard", absolute: 525600, idle: 525600);

        var result = await CreateSut().ResolveAsync(_userId, "tala-v2-dashboard", PlatformConfig());

        Assert.Equal(app.Id, result.AppId);
        Assert.Equal(525600, result.AbsoluteTimeoutMinutes);
        Assert.Equal(525600, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldScopeOverrideToTheGrantedAppOnly()
    {
        // A user granted to a long-session app must not carry that lifetime into another app.
        // finvoice gets its OWN distinct override so this discriminates three ways: leaking
        // Tala's policy gives 525600, ignoring finvoice's gives 480, only correct per-app
        // scoping gives 1440.
        SetupApp("tala-v2-dashboard", absolute: 525600, idle: 525600);
        SetupApp("finvoice", absolute: 1440, idle: 60);

        var result = await CreateSut().ResolveAsync(_userId, "finvoice", PlatformConfig());

        Assert.Equal(1440, result.AbsoluteTimeoutMinutes);
        Assert.Equal(60, result.IdleTimeoutMinutes);
    }

    // ── Override shapes ──────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ShouldUsePlatformDefaults_WhenAppHasNoOverrides()
    {
        var app = SetupApp("finvoice");

        var result = await CreateSut().ResolveAsync(_userId, "finvoice", PlatformConfig());

        // AppId is still stamped — every login identifies its app, override or not.
        Assert.Equal(app.Id, result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldApplyOverridesIndependently()
    {
        SetupApp("kiosk", absolute: 10080, idle: null);

        var result = await CreateSut().ResolveAsync(_userId, "kiosk", PlatformConfig());

        Assert.Equal(10080, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Resolve_ShouldIgnoreNonPositiveOverrides(int bad)
    {
        // A zero or negative timeout would expire every session for the app on creation.
        SetupApp("broken", absolute: bad, idle: bad);

        var result = await CreateSut().ResolveAsync(_userId, "broken", PlatformConfig());

        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    // ── Platform config fallbacks ────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ShouldFallBackToHardcodedDefaults_WhenConfigKeysMissing()
    {
        var result = await CreateSut().ResolveAsync(_userId, null, new Dictionary<string, string>());

        Assert.Equal(SessionPolicyResolver.DefaultAbsoluteTimeoutMinutes, result.AbsoluteTimeoutMinutes);
        Assert.Equal(SessionPolicyResolver.DefaultIdleTimeoutMinutes, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldFallBackToHardcodedDefaults_WhenConfigValuesUnparseable()
    {
        var result = await CreateSut().ResolveAsync(_userId, null, PlatformConfig(absolute: "abc", idle: ""));

        Assert.Equal(SessionPolicyResolver.DefaultAbsoluteTimeoutMinutes, result.AbsoluteTimeoutMinutes);
        Assert.Equal(SessionPolicyResolver.DefaultIdleTimeoutMinutes, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldFallBackToHardcodedDefaults_WhenPlatformConfigIsNonPositive()
    {
        var result = await CreateSut().ResolveAsync(_userId, null, PlatformConfig(absolute: "0", idle: "-5"));

        Assert.Equal(SessionPolicyResolver.DefaultAbsoluteTimeoutMinutes, result.AbsoluteTimeoutMinutes);
        Assert.Equal(SessionPolicyResolver.DefaultIdleTimeoutMinutes, result.IdleTimeoutMinutes);
    }

    // ── ResolveForAuthorisedApp (federated path) ─────────────────────────────

    [Fact]
    public void ResolveForAuthorisedApp_ShouldApplyOverrideWithoutGrantLookup()
    {
        // FederatedLoginService verifies the grant itself before calling this overload.
        var app = new App
        {
            Id = Guid.NewGuid(), Slug = "tala-v2-dashboard", Status = EntityStatus.Active,
            SessionAbsoluteTimeoutMinutes = 525600, SessionIdleTimeoutMinutes = 525600
        };

        var result = CreateSut().ResolveForAuthorisedApp(app, PlatformConfig());

        Assert.Equal(app.Id, result.AppId);
        Assert.Equal(525600, result.AbsoluteTimeoutMinutes);
        Assert.Equal(525600, result.IdleTimeoutMinutes);
        _grants.Verify(g => g.GetActiveByUserAndAppAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData(EntityStatus.Suspended)]
    [InlineData(EntityStatus.Inactive)]
    public void ResolveForAuthorisedApp_ShouldRefuseOverride_WhenAppIsNotActive(string status)
    {
        // Suspending an app is the operator's kill switch for its long-lived sessions.
        // It must hold on the federated path too, which authorises its own app and never
        // checks app status itself.
        var app = new App
        {
            Id = Guid.NewGuid(), Slug = "tala-v2-dashboard", Status = status,
            SessionAbsoluteTimeoutMinutes = 525600, SessionIdleTimeoutMinutes = 525600
        };

        var result = CreateSut().ResolveForAuthorisedApp(app, PlatformConfig());

        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
        // The association itself was verified by the caller, so the app id stands.
        Assert.Equal(app.Id, result.AppId);
    }

    [Fact]
    public void ResolveForAuthorisedApp_ShouldUsePlatformDefaults_WhenAppIsNull()
    {
        var result = CreateSut().ResolveForAuthorisedApp(null, PlatformConfig());

        Assert.Null(result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }
}
