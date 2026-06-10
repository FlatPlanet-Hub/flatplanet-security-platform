using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class ServiceTokenRepository : IServiceTokenRepository
{
    private readonly IDbConnectionFactory _db;

    public ServiceTokenRepository(IDbConnectionFactory db) => _db = db;

    public async Task<ServiceToken?> GetByHashAsync(string tokenHash)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ServiceToken>(
            "SELECT * FROM service_tokens WHERE token_hash = @TokenHash AND status = 'active'",
            new { TokenHash = tokenHash });
    }

    public async Task<ServiceToken?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ServiceToken>(
            "SELECT * FROM service_tokens WHERE id = @Id", new { Id = id });
    }

    public async Task<ServiceToken?> GetByServiceNameAsync(string serviceName)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ServiceToken>(
            "SELECT * FROM service_tokens WHERE service_name = @ServiceName",
            new { ServiceName = serviceName });
    }

    public async Task<IEnumerable<ServiceToken>> GetAllAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<ServiceToken>(
            "SELECT * FROM service_tokens ORDER BY created_at DESC");
    }

    public async Task<ServiceToken> CreateAsync(ServiceToken token)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<ServiceToken>(
            """
            INSERT INTO service_tokens (service_name, token_hash, scopes, description, created_by)
            VALUES (@ServiceName, @TokenHash, @Scopes, @Description, @CreatedBy)
            RETURNING *
            """, token);
    }

    public async Task UpdateScopesAsync(Guid id, string[] scopes)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE service_tokens SET scopes = @Scopes WHERE id = @Id",
            new { Id = id, Scopes = scopes });
    }

    public async Task RevokeAsync(Guid id, Guid revokedBy)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE service_tokens
               SET status = 'revoked', revoked_at = now(), revoked_by = @RevokedBy
             WHERE id = @Id AND status = 'active'
            """,
            new { Id = id, RevokedBy = revokedBy });
    }

    public async Task TouchLastUsedAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        // Best-effort. If it fails (e.g. transient DB error during a request),
        // swallow — last_used_at is observational, not authoritative.
        try
        {
            await conn.ExecuteAsync(
                "UPDATE service_tokens SET last_used_at = now() WHERE id = @Id",
                new { Id = id });
        }
        catch
        {
            // Intentionally swallowed: last_used_at is best-effort observational data.
        }
    }
}
