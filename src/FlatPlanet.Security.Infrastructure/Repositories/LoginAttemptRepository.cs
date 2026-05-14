using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class LoginAttemptRepository : ILoginAttemptRepository
{
    private readonly IDbConnectionFactory _db;

    public LoginAttemptRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task RecordAsync(LoginAttempt attempt)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO login_attempts (email, ip_address, success, attempted_at) VALUES (@Email, @IpAddress, @Success, @AttemptedAt)",
            attempt);
    }

    public async Task<LoginCheckCounts> GetLoginChecksAsync(
        string email, string? ipAddress,
        DateTime ipSince, DateTime emailSince, DateTime lockoutSince)
    {
        var oldestSince = ipSince < emailSince
            ? (ipSince < lockoutSince ? ipSince : lockoutSince)
            : (emailSince < lockoutSince ? emailSince : lockoutSince);

        using var conn = await _db.CreateConnectionAsync();
        var row = await conn.QuerySingleAsync(
            """
            SELECT
              COUNT(*) FILTER (WHERE ip_address = @IpAddress AND success = false AND attempted_at >= @IpSince)     AS ip_failures,
              COUNT(*) FILTER (WHERE LOWER(email) = LOWER(@Email) AND attempted_at >= @EmailSince)                  AS email_attempts,
              COUNT(*) FILTER (WHERE LOWER(email) = LOWER(@Email) AND success = false AND attempted_at >= @LockoutSince) AS lockout_failures
            FROM login_attempts
            WHERE attempted_at >= @OldestSince
            """,
            new { IpAddress = ipAddress ?? "", Email = email, IpSince = ipSince, EmailSince = emailSince, LockoutSince = lockoutSince, OldestSince = oldestSince });

        return new LoginCheckCounts(
            IpFailures:      (int)(long)row.ip_failures,
            EmailAttempts:   (int)(long)row.email_attempts,
            LockoutFailures: (int)(long)row.lockout_failures);
    }

    public async Task<int> CountRecentByEmailAsync(string email, DateTime since)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM login_attempts
            WHERE email = @Email AND attempted_at >= @Since
            """,
            new { Email = email, Since = since });
    }

    public async Task<int> CountRecentFailuresByEmailAsync(string email, DateTime since)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM login_attempts
            WHERE email = @Email AND success = false AND attempted_at >= @Since
            """,
            new { Email = email, Since = since });
    }

    public async Task<int> CountRecentFailuresByIpAsync(string ipAddress, DateTime since)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            """
            SELECT COUNT(*) FROM login_attempts
            WHERE ip_address = @IpAddress AND success = false AND attempted_at >= @Since
            """,
            new { IpAddress = ipAddress, Since = since });
    }

    public async Task DeleteOlderThanAsync(int retentionDays)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM login_attempts WHERE attempted_at < NOW() - (@Days || ' days')::INTERVAL",
            new { Days = retentionDays });
    }
}
