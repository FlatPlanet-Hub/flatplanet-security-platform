using System.Data;
using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IDbConnectionFactory _db;

    public RefreshTokenRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<RefreshToken> CreateAsync(RefreshToken token)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO refresh_tokens (user_id, session_id, token_hash, expires_at)
            VALUES (@UserId, @SessionId, @TokenHash, @ExpiresAt)
            RETURNING id
            """,
            token);
        token.Id = id;
        return token;
    }

    public async Task<RefreshToken> CreateAsync(RefreshToken token, IDbConnection conn, IDbTransaction tx)
    {
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO refresh_tokens (user_id, session_id, token_hash, expires_at)
            VALUES (@UserId, @SessionId, @TokenHash, @ExpiresAt)
            RETURNING id
            """,
            token, transaction: tx);
        token.Id = id;
        return token;
    }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<RefreshToken>(
            "SELECT * FROM refresh_tokens WHERE token_hash = @TokenHash",
            new { TokenHash = tokenHash });
    }

    public async Task RevokeAsync(Guid tokenId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked = true, revoked_at = now(), revoked_reason = @Reason
            WHERE id = @Id
            """,
            new { Reason = reason, Id = tokenId });
    }

    public async Task RevokeAllByUserAsync(Guid userId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked = true, revoked_at = now(), revoked_reason = @Reason
            WHERE user_id = @UserId AND revoked = false
            """,
            new { Reason = reason, UserId = userId });
    }

    public async Task RevokeAllByUserAsync(Guid userId, string reason, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked = true, revoked_at = now(), revoked_reason = @Reason
            WHERE user_id = @UserId AND revoked = false
            """,
            new { Reason = reason, UserId = userId },
            transaction: tx);
    }

    public async Task RevokeAllByCompanyIdAsync(Guid companyId, string reason)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked = true, revoked_at = now(), revoked_reason = @Reason
            WHERE user_id IN (SELECT id FROM users WHERE company_id = @CompanyId)
              AND revoked = false
            """,
            new { Reason = reason, CompanyId = companyId });
    }

    public async Task RevokeAllByCompanyIdAsync(Guid companyId, string reason, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked = true, revoked_at = now(), revoked_reason = @Reason
            WHERE user_id IN (SELECT id FROM users WHERE company_id = @CompanyId)
              AND revoked = false
            """,
            new { Reason = reason, CompanyId = companyId },
            transaction: tx);
    }

    public async Task RotateAsync(Guid tokenId, string newTokenHash)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked                 = true,
                revoked_at              = now(),
                revoked_reason          = 'rotated',
                replaced_by_token_hash  = @NewTokenHash,
                rotated_at              = now()
            WHERE id = @Id
            """,
            new { Id = tokenId, NewTokenHash = newTokenHash });
    }

    public async Task RotateAsync(Guid tokenId, string newTokenHash, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            """
            UPDATE refresh_tokens
            SET revoked                 = true,
                revoked_at              = now(),
                revoked_reason          = 'rotated',
                replaced_by_token_hash  = @NewTokenHash,
                rotated_at              = now()
            WHERE id = @Id
            """,
            new { Id = tokenId, NewTokenHash = newTokenHash },
            transaction: tx);
    }

}
