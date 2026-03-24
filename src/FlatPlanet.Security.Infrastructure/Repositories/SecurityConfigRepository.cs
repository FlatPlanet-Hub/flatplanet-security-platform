using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class SecurityConfigRepository : ISecurityConfigRepository
{
    private readonly IDbConnectionFactory _db;

    public SecurityConfigRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT config_value FROM security_config WHERE config_key = @Key",
            new { Key = key });
    }

    public async Task<int> GetIntValueAsync(string key, int defaultValue)
    {
        var value = await GetValueAsync(key);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<IEnumerable<SecurityConfig>> GetAllAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<SecurityConfig>("SELECT * FROM security_config ORDER BY config_key");
    }

    public async Task UpdateAsync(string key, string value, Guid updatedBy)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            """
            UPDATE security_config
            SET config_value = @Value, updated_at = now(), updated_by = @UpdatedBy
            WHERE config_key = @Key
            """,
            new { Value = value, UpdatedBy = updatedBy, Key = key });
    }
}
