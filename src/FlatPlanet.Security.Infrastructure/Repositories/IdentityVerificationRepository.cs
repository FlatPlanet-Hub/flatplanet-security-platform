using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class IdentityVerificationRepository : IIdentityVerificationRepository
{
    private readonly IDbConnectionFactory _db;

    public IdentityVerificationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IdentityVerificationStatus?> GetByUserIdAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<IdentityVerificationStatus>(
            "SELECT * FROM identity_verification_status WHERE user_id = @UserId",
            new { UserId = userId });
    }

    public async Task UpsertAsync(IdentityVerificationStatus status)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO identity_verification_status
                (user_id, otp_verified, video_verified, fully_verified, verified_at, updated_at)
            VALUES
                (@UserId, @OtpVerified, @VideoVerified, @FullyVerified, @VerifiedAt, @UpdatedAt)
            ON CONFLICT (user_id) DO UPDATE SET
                otp_verified   = EXCLUDED.otp_verified,
                video_verified = EXCLUDED.video_verified,
                fully_verified = EXCLUDED.fully_verified,
                verified_at    = COALESCE(identity_verification_status.verified_at, EXCLUDED.verified_at),
                updated_at     = EXCLUDED.updated_at
            """,
            status);
    }
}
