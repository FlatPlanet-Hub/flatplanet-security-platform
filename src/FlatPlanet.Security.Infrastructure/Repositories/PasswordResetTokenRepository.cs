using System.Data;
using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly IDbConnectionFactory _db;

    public PasswordResetTokenRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task CreateAsync(PasswordResetToken token)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO password_reset_tokens (user_id, token_hash, expires_at)
            VALUES (@user_id::uuid, @token_hash, @expires_at)
            """,
            new { user_id = token.UserId, token_hash = token.TokenHash, expires_at = token.ExpiresAt });
    }

    public async Task<PasswordResetToken?> GetValidByTokenHashAsync(string tokenHash)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<PasswordResetToken>(
            """
            SELECT * FROM password_reset_tokens
            WHERE token_hash = @token_hash AND used = false AND expires_at > now()
            ORDER BY created_at DESC
            LIMIT 1
            """,
            new { token_hash = tokenHash });
    }

    public async Task MarkAsUsedAsync(Guid tokenId, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            """
            UPDATE password_reset_tokens
            SET used = true, used_at = now()
            WHERE id = @id::uuid
            """,
            new { id = tokenId },
            transaction: tx);
    }

    public async Task InvalidatePendingByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE password_reset_tokens
            SET used = true, used_at = now()
            WHERE user_id = @user_id::uuid AND used = false AND expires_at > now()
            """,
            new { user_id = userId });
    }
}
