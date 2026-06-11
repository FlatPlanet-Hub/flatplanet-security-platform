using System.Security.Cryptography;
using System.Text;
using FlatPlanet.Security.Application.DTOs.ServiceTokens;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Services;
using FlatPlanet.Security.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace FlatPlanet.Security.Tests;

public class ServiceTokenServiceTests
{
    private readonly Mock<IServiceTokenRepository> _repo = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private ServiceTokenService Create() =>
        new(_repo.Object, _audit.Object, _cache);

    private readonly Guid _actingUserId = Guid.NewGuid();

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [Fact]
    public async Task Mint_CreatesToken_WithExpectedFormatAndHashing()
    {
        ServiceToken? captured = null;
        _repo.Setup(r => r.GetByServiceNameAsync("hub-api")).ReturnsAsync((ServiceToken?)null);
        _repo.Setup(r => r.CreateAsync(It.IsAny<ServiceToken>()))
             .Callback<ServiceToken>(t => captured = t)
             .ReturnsAsync((ServiceToken t) =>
             {
                 t.Id = Guid.NewGuid();
                 t.CreatedAt = DateTime.UtcNow;
                 return t;
             });

        var svc = Create();
        var resp = await svc.MintAsync(
            new MintServiceTokenRequest
            {
                ServiceName = "hub-api",
                Scopes = ["bootstrap"],
                Description = "HubApi server-to-server",
            },
            _actingUserId);

        Assert.StartsWith("fps_hub-api_", resp.Token);
        Assert.Equal(["bootstrap"], resp.Scopes);
        Assert.NotNull(captured);
        Assert.Equal(Sha256Hex(resp.Token), captured!.TokenHash);
        _audit.Verify(a => a.LogAsync(It.IsAny<AuthAuditLog>()), Times.Once);
    }

    [Fact]
    public async Task Mint_ThrowsConflict_WhenServiceNameExists()
    {
        _repo.Setup(r => r.GetByServiceNameAsync("hub-api"))
             .ReturnsAsync(new ServiceToken { ServiceName = "hub-api" });

        var svc = Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.MintAsync(new MintServiceTokenRequest { ServiceName = "hub-api", Scopes = [] }, _actingUserId));
    }

    [Fact]
    public async Task Validate_ReturnsToken_WhenActiveMatch()
    {
        const string plaintext = "fps_hub-api_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var hash = Sha256Hex(plaintext);
        var stored = new ServiceToken
        {
            Id = Guid.NewGuid(),
            ServiceName = "hub-api",
            TokenHash = hash,
            Scopes = ["bootstrap"],
            Status = "active",
        };
        _repo.Setup(r => r.GetByHashAsync(hash)).ReturnsAsync(stored);

        var svc = Create();
        var result = await svc.ValidateAsync(plaintext);

        Assert.NotNull(result);
        Assert.Equal(stored.Id, result!.Id);
    }

    [Fact]
    public async Task Validate_ReturnsNull_WhenNotFound()
    {
        _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync((ServiceToken?)null);

        var svc = Create();
        var result = await svc.ValidateAsync("fps_unknown_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Null(result);
    }

    [Fact]
    public async Task Validate_ReturnsNull_WhenWrongPrefix()
    {
        var svc = Create();
        var result = await svc.ValidateAsync("not-a-fps-token");

        Assert.Null(result);
        _repo.Verify(r => r.GetByHashAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Validate_CachesResult_OnSecondCall()
    {
        const string plaintext = "fps_hub-api_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var hash = Sha256Hex(plaintext);
        _repo.Setup(r => r.GetByHashAsync(hash))
             .ReturnsAsync(new ServiceToken { Id = Guid.NewGuid(), TokenHash = hash, Status = "active", Scopes = [] });

        var svc = Create();
        _ = await svc.ValidateAsync(plaintext);
        _ = await svc.ValidateAsync(plaintext);

        _repo.Verify(r => r.GetByHashAsync(hash), Times.Once);  // second call served from cache
    }

    [Fact]
    public async Task UpdateScopes_DropsCache_AndAuditsChange()
    {
        var tokenId = Guid.NewGuid();
        var existing = new ServiceToken
        {
            Id = tokenId,
            ServiceName = "hub-api",
            TokenHash = "abc",
            Scopes = ["bootstrap"],
            Status = "active",
        };
        _repo.Setup(r => r.GetByIdAsync(tokenId)).ReturnsAsync(existing);
        _cache.Set("fp:sec:svctoken:abc", existing);

        var svc = Create();
        await svc.UpdateScopesAsync(tokenId, ["users:read", "apps:read"], _actingUserId);

        Assert.False(_cache.TryGetValue("fp:sec:svctoken:abc", out _));
        _repo.Verify(r => r.UpdateScopesAsync(tokenId, It.Is<string[]>(s => s.Length == 2)), Times.Once);
        _audit.Verify(a => a.LogAsync(It.IsAny<AuthAuditLog>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_DropsCache_AndAuditsChange()
    {
        var tokenId = Guid.NewGuid();
        var existing = new ServiceToken
        {
            Id = tokenId,
            ServiceName = "hub-api",
            TokenHash = "xyz",
            Scopes = ["bootstrap"],
            Status = "active",
        };
        _repo.Setup(r => r.GetByIdAsync(tokenId)).ReturnsAsync(existing);
        _cache.Set("fp:sec:svctoken:xyz", existing);

        var svc = Create();
        await svc.RevokeAsync(tokenId, _actingUserId);

        Assert.False(_cache.TryGetValue("fp:sec:svctoken:xyz", out _));
        _repo.Verify(r => r.RevokeAsync(tokenId, _actingUserId), Times.Once);
        _audit.Verify(a => a.LogAsync(It.IsAny<AuthAuditLog>()), Times.Once);
    }

    [Fact]
    public void Entity_HasScope_BootstrapMatchesAnything()
    {
        var t = new ServiceToken { Status = "active", Scopes = ["bootstrap"] };

        Assert.True(t.HasScope("users:read"));
        Assert.True(t.HasScope("anything-at-all"));
    }

    [Fact]
    public void Entity_HasScope_ExactMatchOnly_WhenNoBootstrap()
    {
        var t = new ServiceToken { Status = "active", Scopes = ["users:read", "apps:read"] };

        Assert.True(t.HasScope("users:read"));
        Assert.True(t.HasScope("apps:read"));
        Assert.False(t.HasScope("users:write"));
    }

    [Fact]
    public void Entity_HasScope_FalseWhenRevoked()
    {
        var t = new ServiceToken { Status = "revoked", Scopes = ["bootstrap"] };
        Assert.False(t.HasScope("anything"));
    }

    [Fact]
    public async Task Mint_RejectsUnknownScope()
    {
        _repo.Setup(r => r.GetByServiceNameAsync(It.IsAny<string>())).ReturnsAsync((ServiceToken?)null);
        var svc = Create();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.MintAsync(
                new MintServiceTokenRequest
                {
                    ServiceName = "hub-api",
                    Scopes = ["users:read", "not-a-real-scope"],
                },
                _actingUserId));

        Assert.Contains("not-a-real-scope", ex.Message);
        _repo.Verify(r => r.CreateAsync(It.IsAny<ServiceToken>()), Times.Never);
    }

    [Fact]
    public async Task Mint_AcceptsAllKnownScopes()
    {
        _repo.Setup(r => r.GetByServiceNameAsync(It.IsAny<string>())).ReturnsAsync((ServiceToken?)null);
        _repo.Setup(r => r.CreateAsync(It.IsAny<ServiceToken>()))
             .ReturnsAsync((ServiceToken t) => { t.Id = Guid.NewGuid(); return t; });

        var svc = Create();
        var resp = await svc.MintAsync(
            new MintServiceTokenRequest { ServiceName = "hub-api", Scopes = ServiceTokenScopes.All },
            _actingUserId);

        Assert.Equal(ServiceTokenScopes.All.Length, resp.Scopes.Length);
    }

    [Fact]
    public async Task UpdateScopes_RejectsUnknownScope()
    {
        var tokenId = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(tokenId)).ReturnsAsync(new ServiceToken
        {
            Id = tokenId, ServiceName = "hub-api", TokenHash = "h", Scopes = ["bootstrap"], Status = "active",
        });

        var svc = Create();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateScopesAsync(tokenId, ["users:read", "bogus:scope"], _actingUserId));

        Assert.Contains("bogus:scope", ex.Message);
        _repo.Verify(r => r.UpdateScopesAsync(It.IsAny<Guid>(), It.IsAny<string[]>()), Times.Never);
    }

    [Fact]
    public void KnownScopes_IncludesBootstrapSentinel()
    {
        Assert.True(ServiceTokenScopes.IsKnown(ServiceToken.BootstrapScope));
        Assert.True(ServiceTokenScopes.IsKnown("USERS:READ")); // case-insensitive
        Assert.False(ServiceTokenScopes.IsKnown("nope"));
    }
}
