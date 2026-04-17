using System.Data;
using Dapper;
using FlatPlanet.Security.Application.DTOs.Users;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _db;

    public UserRepository(IDbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @Id",
            new { Id = id });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE email = @Email",
            new { Email = email });
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<User>("SELECT * FROM users ORDER BY full_name");
    }

    public async Task<IEnumerable<User>> GetByCompanyIdAsync(Guid companyId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<User>(
            "SELECT * FROM users WHERE company_id = @CompanyId",
            new { CompanyId = companyId });
    }

    public async Task<PagedResult<User>> GetPagedAsync(UserQueryParams query)
    {
        var where = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", Math.Clamp(query.PageSize, 1, 100));
        parameters.Add("Offset", (Math.Max(query.Page, 1) - 1) * Math.Clamp(query.PageSize, 1, 100));

        if (query.CompanyId.HasValue)
        {
            where.Add("company_id = @CompanyId");
            parameters.Add("CompanyId", query.CompanyId.Value);
        }
        if (!string.IsNullOrEmpty(query.Status))
        {
            where.Add("status = @Status");
            parameters.Add("Status", query.Status);
        }
        if (!string.IsNullOrEmpty(query.Search))
        {
            where.Add("(email ILIKE @Search OR full_name ILIKE @Search)");
            parameters.Add("Search", $"%{query.Search}%");
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        using var conn = await _db.CreateConnectionAsync();
        var items = await conn.QueryAsync<User>(
            $"SELECT * FROM users {whereClause} ORDER BY full_name LIMIT @PageSize OFFSET @Offset",
            parameters);
        var total = await conn.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM users {whereClause}",
            parameters);

        return new PagedResult<User>
        {
            Items = items,
            TotalCount = total,
            Page = Math.Max(query.Page, 1),
            PageSize = Math.Clamp(query.PageSize, 1, 100)
        };
    }

    public async Task UpdateAsync(User user)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET full_name = @FullName, role_title = @RoleTitle WHERE id = @Id",
            user);
    }

    public async Task UpdateLastSeenAtAsync(Guid userId, DateTime lastSeenAt)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET last_seen_at = @LastSeenAt WHERE id = @Id",
            new { LastSeenAt = lastSeenAt, Id = userId });
    }

    public async Task UpdateStatusAsync(Guid userId, string status)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET status = @Status WHERE id = @Id",
            new { Status = status, Id = userId });
    }

    public async Task UpdateStatusAsync(Guid userId, string status, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            "UPDATE users SET status = @Status WHERE id = @Id",
            new { Status = status, Id = userId },
            transaction: tx);
    }

    public async Task SuspendByCompanyIdAsync(Guid companyId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET status = 'suspended' WHERE company_id = @CompanyId AND status = 'active'",
            new { CompanyId = companyId });
    }

    public async Task SuspendByCompanyIdAsync(Guid companyId, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            "UPDATE users SET status = 'suspended' WHERE company_id = @CompanyId AND status = 'active'",
            new { CompanyId = companyId },
            transaction: tx);
    }

    public async Task DeactivateAllByCompanyIdAsync(Guid companyId, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            "UPDATE users SET status = 'inactive' WHERE company_id = @CompanyId AND status = 'active'",
            new { CompanyId = companyId },
            transaction: tx);
    }

    public async Task<User> CreateAsync(User user)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            "INSERT INTO users (company_id, email, full_name, role_title, password_hash, status) " +
            "VALUES (@CompanyId, @Email, @FullName, @RoleTitle, @PasswordHash, @Status) RETURNING id",
            user);
        user.Id = id;
        return user;
    }

    public async Task UpdatePasswordHashAsync(Guid userId, string passwordHash)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET password_hash = @password_hash WHERE id = @id::uuid",
            new { password_hash = passwordHash, id = userId });
    }

    public async Task UpdatePasswordHashAsync(Guid userId, string passwordHash, IDbConnection conn, IDbTransaction tx)
    {
        await conn.ExecuteAsync(
            "UPDATE users SET password_hash = @password_hash WHERE id = @id::uuid",
            new { password_hash = passwordHash, id = userId },
            transaction: tx);
    }

    public async Task UpdateMfaEnabledAsync(Guid userId, bool enabled)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET mfa_enabled = @Enabled WHERE id = @Id",
            new { Enabled = enabled, Id = userId });
    }

    public async Task UpdateMfaTotpSecretAsync(Guid userId, string encryptedSecret)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET mfa_totp_secret = @EncryptedSecret WHERE id = @Id",
            new { EncryptedSecret = encryptedSecret, Id = userId });
    }

    public async Task SetMfaTotpEnrolledAsync(Guid userId, bool enrolled)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET mfa_totp_enrolled = @Enrolled, mfa_enabled = @Enrolled, mfa_method = CASE WHEN @Enrolled THEN 'totp' ELSE mfa_method END WHERE id = @Id",
            new { Enrolled = enrolled, Id = userId });
    }

    public async Task ResetMfaColumnsAsync(Guid userId)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE users SET mfa_enabled = false, mfa_method = null, mfa_totp_secret = null, mfa_totp_enrolled = false WHERE id = @Id",
            new { Id = userId });
    }
}
