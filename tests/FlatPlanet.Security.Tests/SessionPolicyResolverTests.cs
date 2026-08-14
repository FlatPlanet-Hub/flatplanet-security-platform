using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlatPlanet.Security.Tests;

public class SessionPolicyResolverTests
{
    private readonly Mock<IAppRepository> _apps = new();

    private SessionPolicyResolver CreateSut() =>
        new(_apps.Object, NullLogger<SessionPolicyResolver>.Instance);

    private static Dictionary<string, string> PlatformConfig(
        string absolute = "480", string idle = "30") => new()
    {
        ["session_absolute_timeout_minutes"] = absolute,
        ["session_idle_timeout_minutes"]     = idle,
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_ShouldUsePlatformDefaults_WhenSlugIsAbsent(string? slug)
    {
        var result = await CreateSut().ResolveAsync(slug, PlatformConfig());

        Assert.Null(result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
        _apps.Verify(a => a.GetBySlugAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_ShouldUsePlatformDefaults_WhenAppNotFound()
    {
        _apps.Setup(a => a.GetBySlugAsync("does-not-exist")).ReturnsAsync((App?)null);

        var result = await CreateSut().ResolveAsync("does-not-exist", PlatformConfig());

        Assert.Null(result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldUsePlatformDefaults_WhenAppHasNoOverrides()
    {
        var appId = Guid.NewGuid();
        _apps.Setup(a => a.GetBySlugAsync("finvoice")).ReturnsAsync(new App
        {
            Id   = appId,
            Slug = "finvoice",
            SessionAbsoluteTimeoutMinutes = null,
            SessionIdleTimeoutMinutes     = null
        });

        var result = await CreateSut().ResolveAsync("finvoice", PlatformConfig());

        // AppId is still stamped — every login identifies its app, override or not.
        Assert.Equal(appId, result.AppId);
        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldApplyOverrides_WhenAppHasThem()
    {
        var appId = Guid.NewGuid();
        _apps.Setup(a => a.GetBySlugAsync("tala-v2-dashboard")).ReturnsAsync(new App
        {
            Id   = appId,
            Slug = "tala-v2-dashboard",
            SessionAbsoluteTimeoutMinutes = 525600,
            SessionIdleTimeoutMinutes     = 525600
        });

        var result = await CreateSut().ResolveAsync("tala-v2-dashboard", PlatformConfig());

        Assert.Equal(appId, result.AppId);
        Assert.Equal(525600, result.AbsoluteTimeoutMinutes);
        Assert.Equal(525600, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldApplyOverridesIndependently()
    {
        _apps.Setup(a => a.GetBySlugAsync("kiosk")).ReturnsAsync(new App
        {
            Id   = Guid.NewGuid(),
            Slug = "kiosk",
            SessionAbsoluteTimeoutMinutes = 10080,
            SessionIdleTimeoutMinutes     = null   // idle falls back to the platform value
        });

        var result = await CreateSut().ResolveAsync("kiosk", PlatformConfig());

        Assert.Equal(10080, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Resolve_ShouldIgnoreNonPositiveOverrides(int bad)
    {
        // A zero or negative timeout would expire every session for the app on creation.
        _apps.Setup(a => a.GetBySlugAsync("broken")).ReturnsAsync(new App
        {
            Id   = Guid.NewGuid(),
            Slug = "broken",
            SessionAbsoluteTimeoutMinutes = bad,
            SessionIdleTimeoutMinutes     = bad
        });

        var result = await CreateSut().ResolveAsync("broken", PlatformConfig());

        Assert.Equal(480, result.AbsoluteTimeoutMinutes);
        Assert.Equal(30, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldFallBackToHardcodedDefaults_WhenConfigKeysMissing()
    {
        var result = await CreateSut().ResolveAsync(null, new Dictionary<string, string>());

        Assert.Equal(SessionPolicyResolver.DefaultAbsoluteTimeoutMinutes, result.AbsoluteTimeoutMinutes);
        Assert.Equal(SessionPolicyResolver.DefaultIdleTimeoutMinutes, result.IdleTimeoutMinutes);
    }

    [Fact]
    public async Task Resolve_ShouldFallBackToHardcodedDefaults_WhenConfigValuesUnparseable()
    {
        var result = await CreateSut().ResolveAsync(null, PlatformConfig(absolute: "abc", idle: ""));

        Assert.Equal(SessionPolicyResolver.DefaultAbsoluteTimeoutMinutes, result.AbsoluteTimeoutMinutes);
        Assert.Equal(SessionPolicyResolver.DefaultIdleTimeoutMinutes, result.IdleTimeoutMinutes);
    }
}
