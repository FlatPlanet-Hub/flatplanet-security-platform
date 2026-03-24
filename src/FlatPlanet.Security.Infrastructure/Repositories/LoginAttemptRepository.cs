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
}
