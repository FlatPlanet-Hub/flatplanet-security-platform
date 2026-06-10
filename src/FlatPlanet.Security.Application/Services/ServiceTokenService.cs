using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlatPlanet.Security.Application.DTOs.ServiceTokens;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Application.Interfaces.Services;
using FlatPlanet.Security.Domain.Entities;
using FlatPlanet.Security.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace FlatPlanet.Security.Application.Services;

public class ServiceTokenService : IServiceTokenService
{
    private readonly IServiceTokenRepository _repo;
    private readonly IAuditLogRepository _audit;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan ValidatorCacheTtl = TimeSpan.FromSeconds(60);
    private const string CacheKeyPrefix = "fp:sec:svctoken:";
    private const string TokenPrefix = "fps";

    public ServiceTokenService(
        IServiceTokenRepository repo,
        IAuditLogRepository audit,
        IMemoryCache cache)
    {
        _repo = repo;
        _audit = audit;
        _cache = cache;
    }

    public async Task<MintServiceTokenResponse> MintAsync(MintServiceTokenRequest request, Guid actingUserId)
    {
        var serviceName = request.ServiceName.Trim().ToLowerInvariant();

        var existing = await _repo.GetByServiceNameAsync(serviceName);
        if (existing is not null)
            throw new InvalidOperationException($"A service token already exists for '{serviceName}'. Revoke it first.");

        // 32 random bytes → 43-char URL-safe base64 (no padding).
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var randomPart = Base64UrlEncode(randomBytes);
        var plaintext = $"{TokenPrefix}_{serviceName}_{randomPart}";

        var hash = Sha256Hex(plaintext);

        var entity = new ServiceToken
        {
            ServiceName = serviceName,
            TokenHash = hash,
            Scopes = request.Scopes ?? [],
            Description = request.Description,
            CreatedBy = actingUserId,
        };
        var created = await _repo.CreateAsync(entity);

        await _audit.LogAsync(new AuthAuditLog
        {
            UserId = actingUserId,
            EventType = AuditEventType.ServiceTokenMinted,
            Details = JsonSerializer.Serialize(new
            {
                serviceTokenId = created.Id,
                serviceName    = created.ServiceName,
                scopes         = created.Scopes,
                description    = created.Description,
            }),
        });

        return new MintServiceTokenResponse
        {
            Id          = created.Id,
            ServiceName = created.ServiceName,
            Scopes      = created.Scopes,
            Description = created.Description,
            Token       = plaintext,
            CreatedAt   = created.CreatedAt,
        };
    }

    public async Task<IEnumerable<ServiceTokenResponse>> ListAsync()
    {
        var all = await _repo.GetAllAsync();
        return all.Select(ToResponse);
    }

    public async Task<ServiceTokenResponse?> GetByIdAsync(Guid id)
    {
        var t = await _repo.GetByIdAsync(id);
        return t is null ? null : ToResponse(t);
    }

    public async Task UpdateScopesAsync(Guid id, string[] scopes, Guid actingUserId)
    {
        var existing = await _repo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Service token {id} not found.");

        var newScopes = scopes ?? [];
        await _repo.UpdateScopesAsync(id, newScopes);
        // Drop cache so the new scope set takes effect on the next request.
        _cache.Remove(CacheKeyHash(existing.TokenHash));

        await _audit.LogAsync(new AuthAuditLog
        {
            UserId = actingUserId,
            EventType = AuditEventType.ServiceTokenScopesChanged,
            Details = JsonSerializer.Serialize(new
            {
                serviceTokenId = id,
                serviceName    = existing.ServiceName,
                oldScopes      = existing.Scopes,
                newScopes,
            }),
        });
    }

    public async Task RevokeAsync(Guid id, Guid actingUserId)
    {
        var existing = await _repo.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Service token {id} not found.");

        if (existing.Status != "active")
            return; // idempotent — already revoked

        await _repo.RevokeAsync(id, actingUserId);
        _cache.Remove(CacheKeyHash(existing.TokenHash));

        await _audit.LogAsync(new AuthAuditLog
        {
            UserId = actingUserId,
            EventType = AuditEventType.ServiceTokenRevoked,
            Details = JsonSerializer.Serialize(new
            {
                serviceTokenId = id,
                serviceName    = existing.ServiceName,
            }),
        });
    }

    public async Task FlushCacheAsync(Guid id)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return;
        _cache.Remove(CacheKeyHash(existing.TokenHash));
    }

    public async Task<ServiceToken?> ValidateAsync(string plaintextToken)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken))
            return null;
        if (!plaintextToken.StartsWith($"{TokenPrefix}_", StringComparison.Ordinal))
            return null;

        var hash = Sha256Hex(plaintextToken);
        var cacheKey = CacheKeyHash(hash);

        // Fast path: cached. Cache stores either a ServiceToken (active hit)
        // or a sentinel "miss" so we don't hammer DB on repeated bad tokens.
        if (_cache.TryGetValue(cacheKey, out object? cached))
        {
            if (cached is ServiceToken cachedToken)
            {
                _ = _repo.TouchLastUsedAsync(cachedToken.Id); // fire-and-forget
                return cachedToken;
            }
            return null; // cached miss
        }

        var fromDb = await _repo.GetByHashAsync(hash);
        if (fromDb is null)
        {
            _cache.Set(cacheKey, "__miss__", ValidatorCacheTtl);
            return null;
        }

        _cache.Set(cacheKey, fromDb, ValidatorCacheTtl);
        _ = _repo.TouchLastUsedAsync(fromDb.Id);
        return fromDb;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CacheKeyHash(string tokenHash) => CacheKeyPrefix + tokenHash;

    private static ServiceTokenResponse ToResponse(ServiceToken t) => new()
    {
        Id          = t.Id,
        ServiceName = t.ServiceName,
        Scopes      = t.Scopes,
        Description = t.Description,
        Status      = t.Status,
        CreatedAt   = t.CreatedAt,
        CreatedBy   = t.CreatedBy,
        RevokedAt   = t.RevokedAt,
        LastUsedAt  = t.LastUsedAt,
    };
}
