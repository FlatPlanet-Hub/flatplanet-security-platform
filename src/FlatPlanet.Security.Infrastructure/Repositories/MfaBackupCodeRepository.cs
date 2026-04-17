using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class MfaBackupCodeRepository : IMfaBackupCodeRepository
{
    private readonly IDbConnectionFactory _db;

    public MfaBackupCodeRepository(IDbConnectionFactory db) => _db = db;

    public async Task CreateManyAsync(IEnumerable<MfaBackupCode> codes)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO mfa_backup_codes (user_id, code_hash) VALUES (@UserId, @CodeHash)",
            codes);
    }

    public async Task<MfaBackupCode?> GetUnusedByUserAndHashAsync(Guid userId, string codeHash)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<MfaBackupCode>(
            "SELECT * FROM mfa_backup_codes WHERE user_id = @UserId AND code_hash = @CodeHash AND used_at IS NULL",
            new { UserId = userId, CodeHash = codeHash });
    }

    public async Task MarkUsedAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE mfa_backup_codes SET used_at = now() WHERE id = @Id",
            new { Id = id });
    }

    public async Task ReplaceAllAsync(Guid userId, IEnumerable<MfaBackupCode> codes)
    {
        using var conn = await _db.CreateConnectionAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(
                "DELETE FROM mfa_backup_codes WHERE user_id = @UserId",
                new { UserId = userId }, tx);
            await conn.ExecuteAsync(
                "INSERT INTO mfa_backup_codes (user_id, code_hash) VALUES (@UserId, @CodeHash)",
                codes, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteAllByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM mfa_backup_codes WHERE user_id = @UserId",
            new { UserId = userId });
    }

    public async Task<int> CountUnusedByUserAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM mfa_backup_codes WHERE user_id = @UserId AND used_at IS NULL",
            new { UserId = userId });
    }
}
