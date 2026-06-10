using FlatPlanet.Security.Application.DTOs.ServiceTokens;

namespace FlatPlanet.Security.Application.Interfaces.Services;

public interface IServiceTokenService
{
    /// <summary>
    /// Mint a new service token. Returns the plaintext value exactly once — it cannot be
    /// retrieved again after this call. Stored as a SHA-256 hash internally.
    /// </summary>
    Task<MintServiceTokenResponse> MintAsync(MintServiceTokenRequest request, Guid actingUserId);

    Task<IEnumerable<ServiceTokenResponse>> ListAsync();

    Task<ServiceTokenResponse?> GetByIdAsync(Guid id);

    Task UpdateScopesAsync(Guid id, string[] scopes, Guid actingUserId);

    Task RevokeAsync(Guid id, Guid actingUserId);

    /// <summary>
    /// Force the validator cache to drop this token's claims. Use for urgent
    /// (suspected-leak) revocation that can't wait for the 60s TTL.
    /// </summary>
    Task FlushCacheAsync(Guid id);

    /// <summary>
    /// Validate a plaintext bearer token. Returns the matching service token if active,
    /// otherwise null. Bumps last_used_at fire-and-forget. Cached in IMemoryCache for 60s.
    /// </summary>
    Task<Domain.Entities.ServiceToken?> ValidateAsync(string plaintextToken);
}
