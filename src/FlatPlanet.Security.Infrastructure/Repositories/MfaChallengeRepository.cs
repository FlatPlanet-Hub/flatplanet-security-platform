using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class MfaChallengeRepository : IMfaChallengeRepository
{
    private readonly IDbConnectionFactory _db;

    public MfaChallengeRepository(IDbConnectionFactory db) => _db = db;

    public async Task<MfaChallenge> CreateAsync(MfaChallenge challenge)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO mfa_challenges (user_id, phone_number, otp_hash, expires_at)
            VALUES (@UserId, @PhoneNumber, @OtpHash, @ExpiresAt)
            RETURNING id
            """,
            challenge);
        challenge.Id = id;
        return challenge;
    }

    public async Task<MfaChallenge?> GetActiveByUserIdAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<MfaChallenge>(
            """
            SELECT * FROM mfa_challenges
            WHERE user_id = @UserId AND verified_at IS NULL AND expires_at > now()
            ORDER BY created_at DESC LIMIT 1
            """,
            new { UserId = userId });
    }

    public async Task<MfaChallenge?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<MfaChallenge>(
            "SELECT * FROM mfa_challenges WHERE id = @Id",
            new { Id = id });
    }

    public async Task MarkVerifiedAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE mfa_challenges SET verified_at = now() WHERE id = @Id",
            new { Id = id });
    }

    public async Task IncrementAttemptsAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE mfa_challenges SET attempts = attempts + 1 WHERE id = @Id",
            new { Id = id });
    }

    public async Task InvalidateActiveAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE mfa_challenges SET expires_at = now()
            WHERE user_id = @UserId AND verified_at IS NULL AND expires_at > now()
            """,
            new { UserId = userId });
    }

    public async Task<bool> HasVerifiedChallengeAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM mfa_challenges WHERE user_id = @UserId AND verified_at IS NOT NULL)",
            new { UserId = userId });
    }

    public async Task DeleteExpiredAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM mfa_challenges WHERE expires_at < now() - INTERVAL '24 hours'");
    }
}
